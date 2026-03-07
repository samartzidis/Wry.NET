//! wry-native: Cross-platform webview C API built on wry + tao.
//!
//! This crate produces a cdylib (.dll / .so / .dylib) that exposes a flat C API
//! for creating native webview windows. It is designed for consumption from C#
//! via P/Invoke, but works from any language with C FFI.
//!
//! # Usage pattern ("Configure, Run, Respond")

#![allow(clippy::missing_safety_doc)]

use std::borrow::Cow;
use std::collections::HashMap;
use std::ffi::{c_char, c_int, c_void, CStr, CString};
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::Arc;

/// Log a wry Result error to stderr if it failed. Used instead of `let _ =`
/// so that errors are visible in debug output.
#[macro_export]
macro_rules! log_err {
    ($expr:expr, $ctx:expr) => {
        if let Err(e) = $expr {
            eprintln!("[wry-native] {} failed: {}", $ctx, e);
        }
    };
}

use tao::dpi::{LogicalPosition, LogicalSize};
use tao::event::{Event, StartCause, WindowEvent};
use tao::event_loop::{ControlFlow, EventLoop, EventLoopBuilder, EventLoopProxy, EventLoopWindowTarget};
use tao::platform::run_return::EventLoopExtRunReturn;
use tao::window::{Fullscreen, Icon, Theme, Window, WindowBuilder as TaoWindowBuilder, WindowId};

use wry::{webview_version, WebContext, WebView, WebViewBuilder};

#[cfg(target_os = "windows")]
use tao::platform::windows::WindowBuilderExtWindows;
#[cfg(target_os = "windows")]
use wry::WebViewBuilderExtWindows;

mod dialog;
mod tray;
use tray::{WryTray, TrayDispatchCallback};

// ---------------------------------------------------------------------------
// Callback type aliases (C function pointers)
// ---------------------------------------------------------------------------

/// IPC message callback: fn(message: *const c_char, url: *const c_char, ctx: *mut c_void)
/// `url` is the origin URL of the page that sent the message.
type IpcCallback = extern "C" fn(*const c_char, *const c_char, *mut c_void);

/// Custom protocol handler:
///   fn(url: *const c_char, method: *const c_char,
///      headers: *const c_char, body: *const u8, body_len: c_int,
///      ctx: *mut c_void, responder: *mut c_void)
///
/// - `url`: full request URI
/// - `method`: HTTP method (e.g. "GET", "POST")
/// - `headers`: request headers as "Key: Value\r\n" pairs (UTF-8 C string)
/// - `body`: request body bytes (may be null if empty)
/// - `body_len`: length of body in bytes
///
/// The handler must call `wry_protocol_respond` with the responder pointer to
/// deliver the response. If it does not, the request will hang.
type ProtocolHandlerCallback =
    extern "C" fn(*const c_char, *const c_char, *const c_char, *const u8, c_int, *mut c_void, *mut c_void);

/// Window close requested callback: fn(ctx: *mut c_void) -> bool
/// Return true to allow the close, false to prevent it.
type CloseCallback = extern "C" fn(*mut c_void) -> bool;

/// Window resized callback: fn(width: c_int, height: c_int, ctx: *mut c_void)
type ResizeCallback = extern "C" fn(c_int, c_int, *mut c_void);

/// Window moved callback: fn(x: c_int, y: c_int, ctx: *mut c_void)
type MoveCallback = extern "C" fn(c_int, c_int, *mut c_void);

/// Window focus changed callback: fn(focused: bool, ctx: *mut c_void)
type FocusCallback = extern "C" fn(bool, *mut c_void);

/// Dispatch callback: fn(window: *mut WryWindow, ctx: *mut c_void)
type DispatchCallback = extern "C" fn(*mut WryWindow, *mut c_void);

/// Exit requested callback: fn(has_code: bool, code: c_int, ctx: *mut c_void) -> bool
/// Called when all windows are closed or when wry_app_exit is called.
/// - `has_code` false: user-initiated (last window closed)
/// - `has_code` true: programmatic exit via wry_app_exit, `code` is the exit code
/// Return true to allow exit, false to prevent.
type ExitRequestedCallback = extern "C" fn(bool, c_int, *mut c_void) -> bool;

/// Window created callback: fn(ctx: *mut c_void, window_id: usize, window_ptr: *mut WryWindow)
/// Called when a window has been materialized and is live (initial or dynamic).
type WindowCreatedCallback = extern "C" fn(*mut c_void, usize, *mut WryWindow);

/// Window creation error callback: fn(ctx: *mut c_void, window_id: usize, error_message: *const c_char)
/// Called when dynamic window creation fails (async path). error_message is UTF-8, may be null.
type WindowCreationErrorCallback = extern "C" fn(*mut c_void, usize, *const c_char);

/// Window destroyed callback: fn(ctx: *mut c_void, window_id: usize)
/// Called when a window has been destroyed (platform Destroyed event - e.g. user closed or OS destroyed with owner).
type WindowDestroyedCallback = extern "C" fn(*mut c_void, usize);

/// Monitor enumeration callback:
///   fn(x: c_int, y: c_int, width: c_int, height: c_int, scale: f64, ctx: *mut c_void)
/// Called once per monitor. Position is the top-left corner in physical pixels.
/// Size is in physical pixels. Scale is the DPI scale factor.
type MonitorCallback = extern "C" fn(c_int, c_int, c_int, c_int, f64, *mut c_void);

/// Navigation handler callback: fn(url: *const c_char, ctx: *mut c_void) -> bool
/// Called before each navigation. Return true to allow, false to block.
type NavigationCallback = extern "C" fn(*const c_char, *mut c_void) -> bool;

/// Page load event callback: fn(event: c_int, url: *const c_char, ctx: *mut c_void)
/// event: 0 = Started, 1 = Finished
type PageLoadCallback = extern "C" fn(c_int, *const c_char, *mut c_void);

/// Evaluate-script result callback: fn(result: *const c_char, ctx: *mut c_void)
/// result is the JSON-encoded return value from the evaluated script.
type EvalResultCallback = extern "C" fn(*const c_char, *mut c_void);

/// Drag-drop event callback:
///   fn(event_type: c_int, paths: *const *const c_char, path_count: c_int,
///      x: c_int, y: c_int, ctx: *mut c_void) -> bool
///
/// - `event_type`: 0=Enter, 1=Over, 2=Drop, 3=Leave
/// - `paths`: array of UTF-8 file path strings (null for Over/Leave)
/// - `path_count`: number of paths (0 for Over/Leave)
/// - `x`, `y`: cursor position relative to the webview
///
/// Return true to block the OS default drag-drop behavior.
type DragDropCallback =
    extern "C" fn(c_int, *const *const c_char, c_int, c_int, c_int, *mut c_void) -> bool;

// ---------------------------------------------------------------------------
// UserEvent -- messages sent to the event loop from any thread
// ---------------------------------------------------------------------------

pub(crate) enum UserEvent {
    /// Execute a C callback on the event loop thread for a window.
    Dispatch {
        window_id: usize,
        callback: DispatchCallback,
        ctx: usize, // *mut c_void stored as usize for Send
    },
    /// Forward a tray icon event from the global handler.
    TrayEvent(tray_icon::TrayIconEvent),
    /// Forward a tray menu event from the global handler.
    TrayMenuEvent(tray_icon::menu::MenuEvent),
    /// Execute a C callback on the event loop thread for a tray.
    TrayDispatch {
        tray_id: usize,
        callback: TrayDispatchCallback,
        ctx: usize,
    },
    /// Remove a tray icon and check exit condition.
    TrayRemove {
        tray_id: usize,
    },
    /// Programmatic exit request via wry_app_exit.
    RequestExit {
        code: c_int,
    },
    /// Create one window from config (posted when wry_window_create is called after run started).
    CreateWindowWithConfig {
        id: usize,
        owner_window_id: usize,
        parent_window_id: usize,
        payload: Box<WindowCreatePayload>,
    },
}

// Safety: the ctx pointer is opaque and only dereferenced by the C caller's
// callback which is responsible for its own thread safety.
unsafe impl Send for UserEvent {}

// ---------------------------------------------------------------------------
// WryWindowConfig -- FFI struct for create-with-config (optional, extend as needed)
// ---------------------------------------------------------------------------

/// One protocol handler entry for WryWindowConfig. scheme and callback must stay valid for the duration of wry_window_create.
#[repr(C)]
pub struct WryProtocolEntry {
    pub scheme: *const c_char,
    pub callback: ProtocolHandlerCallback,
    pub ctx: *mut c_void,
}

/// C ABI config for window creation. Pass to wry_window_create; null = use defaults.
/// All string pointers are UTF-8, null = not set / default. protocols may be null if protocol_count is 0.
#[repr(C)]
pub struct WryWindowConfig {
    pub title: *const c_char,
    pub url: *const c_char,
    pub html: *const c_char,
    pub width: c_int,
    pub height: c_int,
    pub data_directory: *const c_char,
    pub protocol_count: c_int,
    pub protocols: *const WryProtocolEntry,
    /// 0 = false, non-zero = true. Windows only; ignored on other platforms.
    pub default_context_menus: c_int,
    /// Window icon: pointer to image file bytes (PNG, ICO, JPEG, BMP, GIF). null or len 0 = no icon.
    pub icon_data: *const u8,
    pub icon_data_len: c_int,
    /// Init scripts: array of UTF-8 C strings injected before page load. null or count 0 = none.
    pub init_script_count: c_int,
    pub init_scripts: *const *const c_char,

    // Event callbacks: function pointer + opaque context. Null function pointer = not set.
    pub ipc_handler: Option<IpcCallback>,
    pub ipc_handler_ctx: *mut c_void,
    pub close_handler: Option<CloseCallback>,
    pub close_handler_ctx: *mut c_void,
    pub resize_handler: Option<ResizeCallback>,
    pub resize_handler_ctx: *mut c_void,
    pub move_handler: Option<MoveCallback>,
    pub move_handler_ctx: *mut c_void,
    pub focus_handler: Option<FocusCallback>,
    pub focus_handler_ctx: *mut c_void,
    pub navigation_handler: Option<NavigationCallback>,
    pub navigation_handler_ctx: *mut c_void,
    pub page_load_handler: Option<PageLoadCallback>,
    pub page_load_handler_ctx: *mut c_void,
    pub drag_drop_handler: Option<DragDropCallback>,
    pub drag_drop_handler_ctx: *mut c_void,
}

/// Build a WindowCreatePayload from FFI config. Safe if config is valid; uses defaults for null/zero.
fn payload_from_config(config: *const WryWindowConfig) -> WindowCreatePayload {
    let mut payload = WindowCreatePayload::default();
    if config.is_null() {
        return payload;
    }
    let c = unsafe { &*config };
    if !c.title.is_null() {
        payload.title = unsafe { c_str_to_string(c.title) };
    }
    if !c.url.is_null() {
        let s = unsafe { c_str_to_string(c.url) };
        if !s.is_empty() {
            payload.url = Some(s);
            payload.html = None;
        }
    }
    if !c.html.is_null() {
        let s = unsafe { c_str_to_string(c.html) };
        if !s.is_empty() {
            payload.html = Some(s);
            payload.url = None;
        }
    }
    if c.width > 0 && c.height > 0 {
        payload.size = (c.width as u32, c.height as u32);
    }
    if !c.data_directory.is_null() {
        let s = unsafe { c_str_to_string(c.data_directory) };
        if !s.is_empty() {
            payload.data_directory = Some(s);
        }
    }
    if c.protocol_count > 0 && !c.protocols.is_null() {
        let slice = unsafe { std::slice::from_raw_parts(c.protocols, c.protocol_count as usize) };
        for entry in slice {
            let scheme = unsafe { c_str_to_string(entry.scheme) };
            if !scheme.is_empty() {
                payload.protocols.push(PendingProtocol {
                    scheme,
                    callback: entry.callback,
                    ctx: entry.ctx as usize,
                });
            }
        }
    }
    #[cfg(target_os = "windows")]
    {
        payload.default_context_menus = c.default_context_menus != 0;
    }
    if !c.icon_data.is_null() && c.icon_data_len > 0 {
        let bytes = unsafe { std::slice::from_raw_parts(c.icon_data, c.icon_data_len as usize) };
        payload.icon = decode_icon_from_bytes(bytes);
    }
    if c.init_script_count > 0 && !c.init_scripts.is_null() {
        let ptrs = unsafe { std::slice::from_raw_parts(c.init_scripts, c.init_script_count as usize) };
        for &ptr in ptrs {
            if !ptr.is_null() {
                let s = unsafe { c_str_to_string(ptr) };
                if !s.is_empty() {
                    payload.init_scripts.push(s);
                }
            }
        }
    }
    if let Some(cb) = c.ipc_handler {
        payload.ipc_handler = Some((cb, c.ipc_handler_ctx as usize));
    }
    if let Some(cb) = c.close_handler {
        payload.close_handler = Some((cb, c.close_handler_ctx as usize));
    }
    if let Some(cb) = c.resize_handler {
        payload.resize_handler = Some((cb, c.resize_handler_ctx as usize));
    }
    if let Some(cb) = c.move_handler {
        payload.move_handler = Some((cb, c.move_handler_ctx as usize));
    }
    if let Some(cb) = c.focus_handler {
        payload.focus_handler = Some((cb, c.focus_handler_ctx as usize));
    }
    if let Some(cb) = c.navigation_handler {
        payload.navigation_handler = Some((cb, c.navigation_handler_ctx as usize));
    }
    if let Some(cb) = c.page_load_handler {
        payload.page_load_handler = Some((cb, c.page_load_handler_ctx as usize));
    }
    if let Some(cb) = c.drag_drop_handler {
        payload.drag_drop_handler = Some((cb, c.drag_drop_handler_ctx as usize));
    }
    payload
}

/// Decode image file bytes (PNG, ICO, JPEG, BMP, GIF) into a window Icon. Used for create-time icon.
fn decode_icon_from_bytes(data: &[u8]) -> Option<Icon> {
    use image::GenericImageView;
    match image::load_from_memory(data) {
        Ok(img) => {
            let rgba = img.to_rgba8();
            let (w, h) = img.dimensions();
            match Icon::from_rgba(rgba.into_raw(), w, h) {
                Ok(icon) => Some(icon),
                Err(e) => {
                    eprintln!("[wry-native] decode_icon_from_bytes: Icon::from_rgba failed: {}", e);
                    None
                }
            }
        }
        Err(e) => {
            eprintln!("[wry-native] decode_icon_from_bytes: image decode failed: {}", e);
            None
        }
    }
}

// ---------------------------------------------------------------------------
// Pending protocol registration
// ---------------------------------------------------------------------------

#[derive(Clone)]
struct PendingProtocol {
    scheme: String,
    callback: ProtocolHandlerCallback,
    ctx: usize,
}

/// Owned configuration for a window, passed at creation time via wry_window_create.
/// Can be sent to the event loop for during-run creation.
#[derive(Clone)]
pub(crate) struct WindowCreatePayload {
    pub title: String,
    pub url: Option<String>,
    pub html: Option<String>,
    pub size: (u32, u32),
    pub min_size: Option<(u32, u32)>,
    pub max_size: Option<(u32, u32)>,
    pub position: Option<(i32, i32)>,
    pub resizable: bool,
    pub fullscreen: bool,
    pub maximized: bool,
    pub minimized: bool,
    pub topmost: bool,
    pub visible: bool,
    pub devtools: bool,
    pub transparent: bool,
    pub decorations: bool,
    pub user_agent: Option<String>,
    pub zoom: f64,
    pub back_forward_gestures: bool,
    pub autoplay: bool,
    pub hotkeys_zoom: bool,
    pub clipboard: bool,
    pub accept_first_mouse: bool,
    pub incognito: bool,
    pub focused: bool,
    pub javascript_disabled: bool,
    pub background_color: Option<(u8, u8, u8, u8)>,
    pub background_throttling: Option<i32>,
    #[cfg(target_os = "windows")]
    pub theme: i32,
    #[cfg(target_os = "windows")]
    pub https_scheme: bool,
    #[cfg(target_os = "windows")]
    pub browser_accelerator_keys: bool,
    #[cfg(target_os = "windows")]
    pub default_context_menus: bool,
    #[cfg(target_os = "windows")]
    pub scroll_bar_style: i32,
    pub skip_taskbar: bool,
    pub content_protected: bool,
    pub shadow: bool,
    pub always_on_bottom: bool,
    pub maximizable: bool,
    pub minimizable: bool,
    pub closable: bool,
    pub focusable: bool,
    #[cfg(target_os = "windows")]
    pub window_classname: Option<String>,
    pub owner_window_id: Option<usize>,
    pub parent_window_id: Option<usize>,
    pub init_scripts: Vec<String>,
    pub protocols: Vec<PendingProtocol>,
    pub data_directory: Option<String>,
    pub icon: Option<Icon>,
    pub ipc_handler: Option<(IpcCallback, usize)>,
    pub close_handler: Option<(CloseCallback, usize)>,
    pub resize_handler: Option<(ResizeCallback, usize)>,
    pub move_handler: Option<(MoveCallback, usize)>,
    pub focus_handler: Option<(FocusCallback, usize)>,
    pub navigation_handler: Option<(NavigationCallback, usize)>,
    pub page_load_handler: Option<(PageLoadCallback, usize)>,
    pub drag_drop_handler: Option<(DragDropCallback, usize)>,
}

impl Default for WindowCreatePayload {
    fn default() -> Self {
        Self {
            title: String::new(),
            url: None,
            html: None,
            size: (800, 600),
            min_size: None,
            max_size: None,
            position: None,
            resizable: true,
            fullscreen: false,
            maximized: false,
            minimized: false,
            topmost: false,
            visible: true,
            devtools: false,
            transparent: false,
            decorations: true,
            user_agent: None,
            zoom: 1.0,
            back_forward_gestures: false,
            autoplay: false,
            hotkeys_zoom: true,
            clipboard: false,
            accept_first_mouse: false,
            incognito: false,
            focused: true,
            javascript_disabled: false,
            background_color: None,
            background_throttling: None,
            #[cfg(target_os = "windows")]
            theme: 0,
            #[cfg(target_os = "windows")]
            https_scheme: false,
            #[cfg(target_os = "windows")]
            browser_accelerator_keys: true,
            #[cfg(target_os = "windows")]
            default_context_menus: true,
            #[cfg(target_os = "windows")]
            scroll_bar_style: 0,
            skip_taskbar: false,
            content_protected: false,
            shadow: true,
            always_on_bottom: false,
            maximizable: true,
            minimizable: true,
            closable: true,
            focusable: true,
            #[cfg(target_os = "windows")]
            window_classname: None,
            owner_window_id: None,
            parent_window_id: None,
            init_scripts: Vec::new(),
            protocols: Vec::new(),
            data_directory: None,
            icon: None,
            ipc_handler: None,
            close_handler: None,
            resize_handler: None,
            move_handler: None,
            focus_handler: None,
            navigation_handler: None,
            page_load_handler: None,
            drag_drop_handler: None,
        }
    }
}

unsafe impl Send for WindowCreatePayload {}

// ---------------------------------------------------------------------------
// WryWindow -- per-window state
// ---------------------------------------------------------------------------

pub struct WryWindow {
    id: usize,

    // Runtime event callbacks (read during event loop, copied from payload in create())
    close_handler: Option<(CloseCallback, usize)>,
    resize_handler: Option<(ResizeCallback, usize)>,
    move_handler: Option<(MoveCallback, usize)>,
    focus_handler: Option<(FocusCallback, usize)>,

    // --- Live objects (populated during create()) ---
    window: Option<Window>,
    webview: Option<WebView>,
    web_context: Option<WebContext>,
    window_id: Option<WindowId>,
}

// Safety: WryWindow is only sent to the main thread when it is pending (window and webview are None).
// WryWindow is only sent to the main thread when pending (window and webview are None).
unsafe impl Send for WryWindow {}

impl WryWindow {
    fn new(id: usize) -> Self {
        Self {
            id,
            close_handler: None,
            resize_handler: None,
            move_handler: None,
            focus_handler: None,
            window: None,
            webview: None,
            web_context: None,
            window_id: None,
        }
    }

    /// Materialize the tao Window + wry WebView from a creation payload.
    /// owner_window / parent_window: resolved parent tao Window; owner takes precedence if both set.
    fn create(
        &mut self,
        payload: &WindowCreatePayload,
        event_loop: &EventLoopWindowTarget<UserEvent>,
        owner_window: Option<&Window>,
        parent_window: Option<&Window>,
    ) -> Result<(), String> {
        let (w, h) = payload.size;
        let mut wb = TaoWindowBuilder::new()
            .with_title(&payload.title)
            .with_inner_size(LogicalSize::new(w, h))
            .with_resizable(payload.resizable)
            .with_always_on_top(payload.topmost)
            .with_visible(payload.visible)
            .with_maximized(payload.maximized)
            .with_decorations(payload.decorations)
            .with_content_protection(payload.content_protected)
            .with_always_on_bottom(payload.always_on_bottom)
            .with_maximizable(payload.maximizable)
            .with_minimizable(payload.minimizable)
            .with_closable(payload.closable)
            .with_focusable(payload.focusable);

        #[cfg(target_os = "windows")]
        {
            wb = wb.with_skip_taskbar(payload.skip_taskbar);
            wb = wb.with_undecorated_shadow(payload.shadow);
            if let Some(ref class_name) = payload.window_classname {
                if !class_name.is_empty() {
                    wb = wb.with_window_classname(class_name);
                }
            }
        }
        #[cfg(not(target_os = "windows"))]
        {
            // skip_taskbar also on Linux (WindowBuilderExtUnix)
            #[cfg(target_os = "linux")]
            {
                use tao::platform::unix::WindowBuilderExtUnix;
                wb = wb.with_skip_taskbar(payload.skip_taskbar);
            }
        }

        if let Some((min_w, min_h)) = payload.min_size {
            wb = wb.with_min_inner_size(LogicalSize::new(min_w, min_h));
        }
        if let Some((max_w, max_h)) = payload.max_size {
            wb = wb.with_max_inner_size(LogicalSize::new(max_w, max_h));
        }
        if let Some((x, y)) = payload.position {
            wb = wb.with_position(LogicalPosition::new(x, y));
        }
        if payload.fullscreen {
            wb = wb.with_fullscreen(Some(Fullscreen::Borderless(None)));
        }
        if let Some(ref icon) = payload.icon {
            wb = wb.with_window_icon(Some(icon.clone()));
        }

        // Owner/parent: Windows = owner_window vs parent_window (HWND); macOS = parent (ns_window); Linux = transient_for (gtk).
        #[cfg(target_os = "windows")]
        {
            if let Some(w) = owner_window {
                use tao::platform::windows::WindowExtWindows;
                wb = wb.with_owner_window(w.hwnd());
            } else if let Some(w) = parent_window {
                use tao::platform::windows::WindowExtWindows;
                wb = wb.with_parent_window(w.hwnd());
            }
        }
        #[cfg(target_os = "macos")]
        {
            if let Some(w) = owner_window.or(parent_window) {
                use tao::platform::macos::{WindowBuilderExtMacOS, WindowExtMacOS};
                wb = wb.with_parent_window(w.ns_window());
            }
        }
        #[cfg(target_os = "linux")]
        {
            if let Some(w) = owner_window.or(parent_window) {
                use tao::platform::unix::{WindowBuilderExtUnix, WindowExtUnix};
                wb = wb.with_transient_for(w.gtk_window());
            }
        }

        let window = wb.build(event_loop).map_err(|e| e.to_string())?;

        if let Some(ref dir) = payload.data_directory {
            self.web_context = Some(WebContext::new(Some(std::path::PathBuf::from(dir))));
        }

        let mut wvb = if let Some(ref mut ctx) = self.web_context {
            WebViewBuilder::new_with_web_context(ctx)
        } else {
            WebViewBuilder::new()
        };

        if let Some(ref url) = payload.url {
            wvb = wvb.with_url(url);
        } else if let Some(ref html) = payload.html {
            wvb = wvb.with_html(html);
        }

        if let Some(ref ua) = payload.user_agent {
            wvb = wvb.with_user_agent(ua);
        }

        if payload.transparent {
            wvb = wvb.with_transparent(true);
        }

        if let Some((r, g, b, a)) = payload.background_color {
            wvb = wvb.with_background_color((r, g, b, a));
        }

        #[cfg(any(debug_assertions, feature = "devtools"))]
        {
            wvb = wvb.with_devtools(payload.devtools);
        }
        let _ = payload.devtools;

        wvb = wvb.with_back_forward_navigation_gestures(payload.back_forward_gestures);
        wvb = wvb.with_autoplay(payload.autoplay);
        wvb = wvb.with_hotkeys_zoom(payload.hotkeys_zoom);
        wvb = wvb.with_clipboard(payload.clipboard);
        wvb = wvb.with_accept_first_mouse(payload.accept_first_mouse);
        wvb = wvb.with_incognito(payload.incognito);
        wvb = wvb.with_focused(payload.focused);

        if payload.javascript_disabled {
            wvb = wvb.with_javascript_disabled();
        }

        if let Some(policy) = payload.background_throttling {
            use wry::BackgroundThrottlingPolicy;
            let p = match policy {
                0 => BackgroundThrottlingPolicy::Disabled,
                1 => BackgroundThrottlingPolicy::Suspend,
                2 => BackgroundThrottlingPolicy::Throttle,
                _ => BackgroundThrottlingPolicy::Suspend,
            };
            wvb = wvb.with_background_throttling(p);
        }

        // Windows-specific builder options
        #[cfg(target_os = "windows")]
        {
            use wry::{Theme, ScrollBarStyle};
            let theme = match payload.theme {
                1 => Theme::Dark,
                2 => Theme::Light,
                _ => Theme::Auto,
            };
            wvb = wvb.with_theme(theme);
            wvb = wvb.with_https_scheme(payload.https_scheme);
            wvb = wvb.with_browser_accelerator_keys(payload.browser_accelerator_keys);
            wvb = wvb.with_default_context_menus(payload.default_context_menus);
            let style = match payload.scroll_bar_style {
                1 => ScrollBarStyle::FluentOverlay,
                _ => ScrollBarStyle::Default,
            };
            wvb = wvb.with_scroll_bar_style(style);
        }

        for script in &payload.init_scripts {
            wvb = wvb.with_initialization_script(script);
        }

        // IPC handler (from payload - baked into webview at creation)
        if let Some((cb, ctx)) = payload.ipc_handler {
            wvb = wvb.with_ipc_handler(move |req| {
                let url = req.uri().to_string();
                let body = req.body();
                if let (Ok(c_body), Ok(c_url)) = (CString::new(body.as_str()), CString::new(url)) {
                    cb(c_body.as_ptr(), c_url.as_ptr(), ctx as *mut c_void);
                }
            });
        }

        // Navigation handler (from payload - baked into webview at creation)
        if let Some((cb, ctx)) = payload.navigation_handler {
            wvb = wvb.with_navigation_handler(move |url| {
                if let Ok(c_url) = CString::new(url.as_str()) {
                    cb(c_url.as_ptr(), ctx as *mut c_void)
                } else {
                    true // allow on encoding error
                }
            });
        }

        // Page load handler (from payload - baked into webview at creation)
        if let Some((cb, ctx)) = payload.page_load_handler {
            use wry::PageLoadEvent;
            wvb = wvb.with_on_page_load_handler(move |event, url| {
                let event_code: c_int = match event {
                    PageLoadEvent::Started => 0,
                    PageLoadEvent::Finished => 1,
                };
                if let Ok(c_url) = CString::new(url.as_str()) {
                    cb(event_code, c_url.as_ptr(), ctx as *mut c_void);
                }
            });
        }

        // Drag-drop handler (from payload - baked into webview at creation)
        if let Some((cb, ctx)) = payload.drag_drop_handler {
            use wry::DragDropEvent;
            wvb = wvb.with_drag_drop_handler(move |event| {
                let (event_type, paths_ref, x, y): (c_int, Option<&Vec<std::path::PathBuf>>, i32, i32) =
                    match &event {
                        DragDropEvent::Enter { paths, position } => (0, Some(paths), position.0, position.1),
                        DragDropEvent::Over { position } => (1, None, position.0, position.1),
                        DragDropEvent::Drop { paths, position } => (2, Some(paths), position.0, position.1),
                        DragDropEvent::Leave => (3, None, 0, 0),
                        _ => return false,
                    };

                let c_strings: Vec<CString> = paths_ref
                    .map(|paths| {
                        paths
                            .iter()
                            .filter_map(|p| CString::new(p.to_string_lossy().as_ref()).ok())
                            .collect()
                    })
                    .unwrap_or_default();
                let c_ptrs: Vec<*const c_char> = c_strings.iter().map(|s| s.as_ptr()).collect();

                let paths_ptr = if c_ptrs.is_empty() {
                    std::ptr::null()
                } else {
                    c_ptrs.as_ptr()
                };
                let path_count = c_ptrs.len() as c_int;

                cb(event_type, paths_ptr, path_count, x as c_int, y as c_int, ctx as *mut c_void)
            });
        }

        for proto in &payload.protocols {
            let cb = proto.callback;
            let ctx = proto.ctx;
            wvb = wvb.with_asynchronous_custom_protocol(proto.scheme.clone(), move |_id, request, responder| {
                // Pack the responder into a heap-allocated box so C can hold it
                let responder_box = Box::new(responder);
                let responder_ptr = Box::into_raw(responder_box) as *mut c_void;

                let uri = request.uri().to_string();
                let method = request.method().as_str().to_string();

                // Serialize headers as "Key: Value\r\n" pairs
                let mut headers_str = String::new();
                for (name, value) in request.headers().iter() {
                    if let Ok(v) = value.to_str() {
                        headers_str.push_str(name.as_str());
                        headers_str.push_str(": ");
                        headers_str.push_str(v);
                        headers_str.push_str("\r\n");
                    }
                }

                let body = request.body();
                let body_ptr = if body.is_empty() { std::ptr::null() } else { body.as_ptr() };
                let body_len = body.len() as c_int;

                if let (Ok(c_uri), Ok(c_method), Ok(c_headers)) = (
                    CString::new(uri),
                    CString::new(method),
                    CString::new(headers_str),
                ) {
                    cb(
                        c_uri.as_ptr(),
                        c_method.as_ptr(),
                        c_headers.as_ptr(),
                        body_ptr,
                        body_len,
                        ctx as *mut c_void,
                        responder_ptr,
                    );
                }
            });
        }

        let webview = wvb
            .build(&window)
            .map_err(|e| e.to_string())?;

        // Apply zoom if not default
        if (payload.zoom - 1.0).abs() > f64::EPSILON {
            log_err!(webview.zoom(payload.zoom), "zoom (init)");
        }

        self.window_id = Some(window.id());
        self.window = Some(window);
        self.webview = Some(webview);
        self.close_handler = payload.close_handler;
        self.resize_handler = payload.resize_handler;
        self.move_handler = payload.move_handler;
        self.focus_handler = payload.focus_handler;

        if payload.minimized {
            if let Some(ref w) = self.window {
                w.set_minimized(true);
            }
        }
        Ok(())
    }
}

// ---------------------------------------------------------------------------
// WryApp -- application-level state
// ---------------------------------------------------------------------------

pub struct WryApp {
    event_loop: Option<EventLoop<UserEvent>>,
    pub(crate) proxy: EventLoopProxy<UserEvent>,
    windows: HashMap<usize, WryWindow>,
    payloads: HashMap<usize, WindowCreatePayload>,
    next_window_id: usize,
    pub(crate) trays: HashMap<usize, WryTray>,
    pub(crate) next_tray_id: usize,
    exit_requested_handler: Option<(ExitRequestedCallback, usize)>,
    /// Set to true when the event loop is running (inside run_return). Used to decide initial vs dynamic window creation.
    run_started: Arc<AtomicBool>,
    /// Called when a window is materialized and live (initial or dynamic).
    window_created_handler: Option<(WindowCreatedCallback, usize)>,
    /// Called when dynamic window creation fails (async path only).
    window_creation_error_handler: Option<(WindowCreationErrorCallback, usize)>,
    window_destroyed_handler: Option<(WindowDestroyedCallback, usize)>,
}

// Safety: WryApp is only accessed from the main thread. The proxy field is
// Send by design (it's the whole point of EventLoopProxy). We need this
// because the raw pointer in wry_window_dispatch comes from any thread but
// only accesses the proxy.
unsafe impl Send for WryApp {}
unsafe impl Sync for WryApp {}

// ---------------------------------------------------------------------------
// Helper: read a C string into a Rust String, returning empty on null.
// ---------------------------------------------------------------------------

pub(crate) unsafe fn c_str_to_string(s: *const c_char) -> String {
    if s.is_null() {
        return String::new();
    }
    CStr::from_ptr(s)
        .to_str()
        .unwrap_or("")
        .to_string()
}

// ---------------------------------------------------------------------------
// ===========================================================================
// EXPORTED C API
// ===========================================================================

// ---------------------------------------------------------------------------
// App lifecycle
// ---------------------------------------------------------------------------

/// Create a new application. Returns an opaque handle.
#[no_mangle]
pub extern "C" fn wry_app_new() -> *mut WryApp {
    let event_loop = EventLoopBuilder::<UserEvent>::with_user_event().build();
    let proxy = event_loop.create_proxy();
    let app = WryApp {
        event_loop: Some(event_loop),
        proxy,
        windows: HashMap::new(),
        payloads: HashMap::new(),
        next_window_id: 1,
        trays: HashMap::new(),
        next_tray_id: 1,
        exit_requested_handler: None,
        run_started: Arc::new(AtomicBool::new(false)),
        window_created_handler: None,
        window_creation_error_handler: None,
        window_destroyed_handler: None,
    };
    Box::into_raw(Box::new(app))
}

/// Run the application event loop. This blocks the calling thread until all
/// windows are closed. Must be called on the main thread.
#[no_mangle]
pub extern "C" fn wry_app_run(app: *mut WryApp) {
    if app.is_null() {
        return;
    }
    let app = unsafe { &mut *app };

    let mut event_loop = match app.event_loop.take() {
        Some(el) => el,
        None => return, // already consumed
    };

    let mut pending_windows: Vec<WryWindow> = app.windows.drain().map(|(_, w)| w).collect();
    let mut pending_payloads: HashMap<usize, WindowCreatePayload> = app.payloads.drain().collect();
    let mut live_windows: HashMap<WindowId, WryWindow> = HashMap::new();
    let mut id_to_window_id: HashMap<usize, WindowId> = HashMap::new();

    // Move trays out of the app struct.
    let mut pending_trays: Vec<WryTray> = app.trays.drain().map(|(_, t)| t).collect();
    let mut live_trays: HashMap<usize, WryTray> = HashMap::new();
    // Map from menu item string ID to tray usize ID for event routing.
    let mut menu_id_to_tray: HashMap<String, usize> = HashMap::new();

    // Exit-requested callback (fired when all windows are closed).
    let exit_requested_handler = app.exit_requested_handler.take();
    let window_created_handler = app.window_created_handler.take();
    let window_creation_error_handler = app.window_creation_error_handler.take();
    let window_destroyed_handler = app.window_destroyed_handler.take();

    let run_started = app.run_started.clone();

    // Wire up tray icon / menu event handlers to forward into the event loop.
    tray::setup_tray_event_handlers(&app.proxy);

    // Use run_return so we return to the caller instead of calling process::exit.
    event_loop.run_return(move |event, event_loop_target, control_flow| {
        *control_flow = ControlFlow::Wait;
        run_started.store(true, Ordering::SeqCst);

        match event {
            Event::NewEvents(StartCause::Init) => {
                pending_windows.sort_by_key(|w| w.id);
                for mut win in pending_windows.drain(..) {
                    let payload = match pending_payloads.remove(&win.id) {
                        Some(p) => p,
                        None => continue,
                    };
                    let owner_window = payload.owner_window_id.and_then(|oid| {
                        id_to_window_id.get(&oid).and_then(|tid| live_windows.get(tid))
                            .and_then(|w| w.window.as_ref())
                    });
                    let parent_window = payload.parent_window_id.and_then(|pid| {
                        id_to_window_id.get(&pid).and_then(|tid| live_windows.get(tid))
                            .and_then(|w| w.window.as_ref())
                    });
                    match win.create(&payload, event_loop_target, owner_window, parent_window) {
                        Ok(()) => {
                            if let Some(wid) = win.window_id {
                                let our_id = win.id;
                                id_to_window_id.insert(our_id, wid);
                                live_windows.insert(wid, win);
                                if let Some((cb, ctx)) = window_created_handler.as_ref() {
                                    if let Some(win_ref) = live_windows.get_mut(&wid) {
                                        cb(*ctx as *mut c_void, our_id, win_ref as *mut WryWindow);
                                    }
                                }
                            }
                        }
                        Err(e) => {
                            let our_id = win.id;
                            if let Some((cb, ctx)) = window_creation_error_handler.as_ref() {
                                if let Ok(c_msg) = CString::new(e.as_str()) {
                                    cb(*ctx as *mut c_void, our_id, c_msg.as_ptr());
                                }
                            }
                        }
                    }
                }
                // Materialize all pending tray icons.
                for mut tray in pending_trays.drain(..) {
                    let our_id = tray.id;
                    tray.create();
                    for mid in &tray.menu_item_ids {
                        menu_id_to_tray.insert(mid.clone(), our_id);
                    }
                    live_trays.insert(our_id, tray);
                }
            }

            Event::WindowEvent {
                event: ref win_event,
                window_id,
                ..
            } => {
                if let Some(win) = live_windows.get_mut(&window_id) {
                    match win_event {
                        WindowEvent::CloseRequested => {
                            let allow = if let Some((cb, ctx)) = win.close_handler {
                                cb(ctx as *mut c_void)
                            } else {
                                true
                            };
                            if allow {
                                let our_id = win.id;
                                id_to_window_id.remove(&our_id);
                                live_windows.remove(&window_id);
                                if live_windows.is_empty() {
                                    let should_exit = if let Some((cb, ctx)) = exit_requested_handler {
                                        cb(false, 0, ctx as *mut c_void)
                                    } else {
                                        true
                                    };
                                    if should_exit {
                                        live_trays.clear();
                                        *control_flow = ControlFlow::Exit;
                                    }
                                }
                            }
                        }
                        WindowEvent::Destroyed => {
                            // Window was destroyed (e.g. by OS when owner closed). Notify C#, then remove from state like Tauri.
                            let our_id = live_windows.get(&window_id).map(|w| w.id);
                            if let Some(oid) = our_id {
                                if let Some((cb, ctx)) = window_destroyed_handler.as_ref() {
                                    cb(*ctx as *mut c_void, oid);
                                }
                                id_to_window_id.remove(&oid);
                                live_windows.remove(&window_id);
                                if live_windows.is_empty() {
                                    let should_exit = if let Some((cb, ctx)) = exit_requested_handler {
                                        cb(false, 0, ctx as *mut c_void)
                                    } else {
                                        true
                                    };
                                    if should_exit {
                                        live_trays.clear();
                                        *control_flow = ControlFlow::Exit;
                                    }
                                }
                            }
                        }
                        WindowEvent::Resized(size) => {
                            if let Some((cb, ctx)) = win.resize_handler {
                                cb(
                                    size.width as c_int,
                                    size.height as c_int,
                                    ctx as *mut c_void,
                                );
                            }
                        }
                        WindowEvent::Moved(pos) => {
                            if let Some((cb, ctx)) = win.move_handler {
                                cb(pos.x as c_int, pos.y as c_int, ctx as *mut c_void);
                            }
                        }
                        WindowEvent::Focused(focused) => {
                            if let Some((cb, ctx)) = win.focus_handler {
                                cb(*focused, ctx as *mut c_void);
                            }
                        }
                        _ => {}
                    }
                }
            }

            Event::UserEvent(user_event) => match user_event {
                UserEvent::Dispatch {
                    window_id: our_id,
                    callback,
                    ctx,
                } => {
                    let mut destroyed_wid = None;
                    if let Some(wid) = id_to_window_id.get(&our_id).copied() {
                        if let Some(win) = live_windows.get_mut(&wid) {
                            let win_ptr = win as *mut WryWindow;
                            callback(win_ptr, ctx as *mut c_void);
                            // If the callback destroyed the window (e.g. wry_window_close),
                            // clean up live_windows so the exit check works.
                            if win.window.is_none() {
                                destroyed_wid = Some(wid);
                            }
                        }
                    }
                    if let Some(wid) = destroyed_wid {
                        live_windows.remove(&wid);
                        if live_windows.is_empty() {
                            let should_exit = if let Some((cb, ctx)) = exit_requested_handler {
                                cb(false, 0, ctx as *mut c_void)
                            } else {
                                true
                            };
                            if should_exit {
                                live_trays.clear();
                                *control_flow = ControlFlow::Exit;
                            }
                        }
                    }
                }

                UserEvent::TrayEvent(ref event) => {
                    if let Ok(our_id) = event.id().as_ref().parse::<usize>() {
                        if let Some(t) = live_trays.get(&our_id) {
                            t.handle_tray_event(event);
                        }
                    }
                }

                UserEvent::TrayMenuEvent(ref event) => {
                    let menu_id: &str = event.id.as_ref();
                    if let Some(&our_id) = menu_id_to_tray.get(menu_id) {
                        if let Some(t) = live_trays.get(&our_id) {
                            t.handle_menu_event(menu_id);
                        }
                    }
                }

                UserEvent::TrayDispatch { tray_id, callback, ctx } => {
                    if let Some(t) = live_trays.get_mut(&tray_id) {
                        t.handle_dispatch(callback, ctx);
                    }
                }

                UserEvent::TrayRemove { tray_id } => {
                    live_trays.remove(&tray_id);
                    if live_windows.is_empty() && live_trays.is_empty() {
                        *control_flow = ControlFlow::Exit;
                    }
                }

                UserEvent::RequestExit { code } => {
                    let should_exit = if let Some((cb, ctx)) = exit_requested_handler {
                        cb(true, code, ctx as *mut c_void)
                    } else {
                        true
                    };
                    if should_exit {
                        live_trays.clear();
                        *control_flow = ControlFlow::Exit;
                    }
                }

                UserEvent::CreateWindowWithConfig {
                    id: our_id,
                    owner_window_id: oid,
                    parent_window_id: pid,
                    payload,
                } => {
                    let owner_window = if oid != 0 {
                        id_to_window_id.get(&oid).and_then(|tid| live_windows.get(tid))
                            .and_then(|w| w.window.as_ref())
                    } else {
                        None
                    };
                    let parent_window = if pid != 0 {
                        id_to_window_id.get(&pid).and_then(|tid| live_windows.get(tid))
                            .and_then(|w| w.window.as_ref())
                    } else {
                        None
                    };
                    let mut win = WryWindow::new(our_id);
                    match win.create(&payload, event_loop_target, owner_window, parent_window) {
                        Ok(()) => {
                            if let Some(wid) = win.window_id {
                                id_to_window_id.insert(our_id, wid);
                                live_windows.insert(wid, win);
                                if let Some((cb, ctx)) = window_created_handler.as_ref() {
                                    if let Some(win_ref) = live_windows.get_mut(&wid) {
                                        cb(*ctx as *mut c_void, our_id, win_ref as *mut WryWindow);
                                    }
                                }
                            }
                        }
                        Err(e) => {
                            if let Some((cb, ctx)) = window_creation_error_handler.as_ref() {
                                if let Ok(c_msg) = CString::new(e.as_str()) {
                                    cb(*ctx as *mut c_void, our_id, c_msg.as_ptr());
                                }
                            }
                        }
                    }
                }
            },

            _ => {}
        }
    });
}

/// Register a callback that fires when all windows have closed or when
/// `wry_app_exit` is called. The callback receives `has_code` (false for
/// user-initiated, true for programmatic), `code` (the exit code when
/// has_code is true), and the context pointer. Return true to allow exit,
/// false to prevent it. Must be called before `wry_app_run`.
#[no_mangle]
pub extern "C" fn wry_app_on_exit_requested(
    app: *mut WryApp,
    callback: ExitRequestedCallback,
    ctx: *mut c_void,
) {
    if app.is_null() { return; }
    let app = unsafe { &mut *app };
    app.exit_requested_handler = Some((callback, ctx as usize));
}

/// Register a callback that fires when a window has been materialized and is live.
/// Called for both initial windows (at startup) and dynamically created windows.
/// Signature: fn(ctx: *mut c_void, window_id: usize, window_ptr: *mut WryWindow).
#[no_mangle]
pub extern "C" fn wry_app_on_window_created(
    app: *mut WryApp,
    callback: WindowCreatedCallback,
    ctx: *mut c_void,
) {
    if app.is_null() { return; }
    let app = unsafe { &mut *app };
    app.window_created_handler = Some((callback, ctx as usize));
}

/// Register a callback that fires when dynamic window creation fails (async path only).
/// Signature: fn(ctx: *mut c_void, window_id: usize, error_message: *const c_char). error_message is UTF-8.
#[no_mangle]
pub extern "C" fn wry_app_on_window_creation_error(
    app: *mut WryApp,
    callback: WindowCreationErrorCallback,
    ctx: *mut c_void,
) {
    if app.is_null() { return; }
    let app = unsafe { &mut *app };
    app.window_creation_error_handler = Some((callback, ctx as usize));
}

/// Register a callback that fires when a window has been destroyed (platform Destroyed event).
/// Signature: fn(ctx: *mut c_void, window_id: usize).
#[no_mangle]
pub extern "C" fn wry_app_on_window_destroyed(
    app: *mut WryApp,
    callback: WindowDestroyedCallback,
    ctx: *mut c_void,
) {
    if app.is_null() { return; }
    let app = unsafe { &mut *app };
    app.window_destroyed_handler = Some((callback, ctx as usize));
}

/// Request the application to exit with the given exit code.
/// This fires the exit-requested callback (if registered) with has_code=true.
/// If the callback allows exit (or none is registered), the event loop exits
/// and any remaining tray icons are removed. Safe to call from any thread.
#[no_mangle]
pub extern "C" fn wry_app_exit(app: *mut WryApp, code: c_int) {
    if app.is_null() { return; }
    let app = unsafe { &*app };
    log_err!(app.proxy.send_event(UserEvent::RequestExit { code }), "request exit");
}

/// Destroy the application handle and free resources.
#[no_mangle]
pub extern "C" fn wry_app_destroy(app: *mut WryApp) {
    if !app.is_null() {
        unsafe {
            drop(Box::from_raw(app));
        }
    }
}

// ---------------------------------------------------------------------------
// Window creation
// ---------------------------------------------------------------------------

/// Create a window with optional config. Pass 0 for owner/parent for top-level.
/// config: null = default params; or pointer to WryWindowConfig for title, url, size, etc.
/// Before run: window is stored in app.windows. After run: posts CreateWindowWithConfig (no queue).
/// Returns window ID (never 0 on success).
#[no_mangle]
pub extern "C" fn wry_window_create(
    app: *mut WryApp,
    owner_window_id: usize,
    parent_window_id: usize,
    config: *const c_void,
) -> usize {
    if app.is_null() {
        return 0;
    }
    let app = unsafe { &mut *app };
    let id = app.next_window_id;
    app.next_window_id += 1;

    let mut payload = if config.is_null() {
        WindowCreatePayload::default()
    } else {
        payload_from_config(config as *const WryWindowConfig)
    };
    if owner_window_id != 0 {
        payload.owner_window_id = Some(owner_window_id);
        payload.parent_window_id = None;
    } else if parent_window_id != 0 {
        payload.parent_window_id = Some(parent_window_id);
        payload.owner_window_id = None;
    }

    if !app.run_started.load(Ordering::SeqCst) {
        let win = WryWindow::new(id);
        app.windows.insert(id, win);
        app.payloads.insert(id, payload);
        return id;
    }

    let _ = app.proxy.send_event(UserEvent::CreateWindowWithConfig {
        id,
        owner_window_id,
        parent_window_id,
        payload: Box::new(payload),
    });
    id
}

// ---------------------------------------------------------------------------
// JavaScript evaluation (post-run: use *mut WryWindow)
// ---------------------------------------------------------------------------

/// Evaluate JavaScript in the webview. Must be called post-run (from a callback
/// or dispatch) with the `*mut WryWindow` pointer.
#[no_mangle]
pub extern "C" fn wry_window_eval_js(win: *mut WryWindow, js: *const c_char) {
    if win.is_null() || js.is_null() {
        return;
    }
    let win = unsafe { &mut *win };
    let js = unsafe { c_str_to_string(js) };
    if let Some(ref wv) = win.webview {
        log_err!(wv.evaluate_script(&js), "evaluate_script");
    }
}

/// Evaluate JavaScript in the webview and receive the result via a callback.
/// The callback receives the JSON-encoded result string (or an error message).
/// Must be called post-run (from a callback or dispatch).
#[no_mangle]
pub extern "C" fn wry_window_eval_js_callback(
    win: *mut WryWindow,
    js: *const c_char,
    callback: EvalResultCallback,
    ctx: *mut c_void,
) {
    if win.is_null() || js.is_null() {
        return;
    }
    let win = unsafe { &mut *win };
    let js = unsafe { c_str_to_string(js) };
    if let Some(ref wv) = win.webview {
        let ctx_usize = ctx as usize;
        log_err!(wv.evaluate_script_with_callback(&js, move |result| {
            match CString::new(result.as_str()) {
                Ok(cs) => {
                    callback(cs.as_ptr(), ctx_usize as *mut c_void);
                }
                Err(_) => {
                    // If the result contains null bytes, pass empty
                    let empty = CString::new("").unwrap();
                    callback(empty.as_ptr(), ctx_usize as *mut c_void);
                }
            };
        }), "evaluate_script_with_callback");
    }
}

/// Respond to a custom protocol request. Must be called exactly once per
/// protocol handler invocation. `responder` is the opaque pointer passed to
/// the protocol handler callback.
///
/// - `data`: pointer to response body bytes
/// - `data_len`: length of response body
/// - `content_type`: MIME type as a UTF-8 C string (e.g. "text/html")
/// - `status_code`: HTTP status code (e.g. 200)
/// - `extra_headers`: additional response headers as "Key: Value\r\n" pairs
///   (UTF-8 C string). Pass null for no extra headers.
#[no_mangle]
pub extern "C" fn wry_protocol_respond(
    responder: *mut c_void,
    data: *const u8,
    data_len: c_int,
    content_type: *const c_char,
    status_code: c_int,
    extra_headers: *const c_char,
) {
    if responder.is_null() {
        return;
    }

    let responder =
        unsafe { Box::from_raw(responder as *mut wry::RequestAsyncResponder) };

    let body: Cow<'static, [u8]> = if data.is_null() || data_len <= 0 {
        Cow::Borrowed(&[])
    } else {
        let slice = unsafe { std::slice::from_raw_parts(data, data_len as usize) };
        Cow::Owned(slice.to_vec())
    };

    let mime = unsafe { c_str_to_string(content_type) };
    let status = if (100..600).contains(&status_code) {
        status_code as u16
    } else {
        200
    };

    let mut builder = http::Response::builder()
        .status(status)
        .header("Content-Type", mime);

    // Parse extra headers ("Key: Value\r\n" pairs)
    if !extra_headers.is_null() {
        let headers_str = unsafe { c_str_to_string(extra_headers) };
        for line in headers_str.split("\r\n") {
            if let Some((key, value)) = line.split_once(": ") {
                let key = key.trim();
                let value = value.trim();
                if !key.is_empty() {
                    builder = builder.header(key, value);
                }
            }
        }
    }

    let response = builder
        .body(body)
        .unwrap_or_else(|_| {
            http::Response::builder()
                .status(500)
                .body(Cow::Borrowed(&[] as &[u8]))
                .unwrap()
        });

    responder.respond(response);
}

// ---------------------------------------------------------------------------
// Window close (post-run: use *mut WryWindow)
// ---------------------------------------------------------------------------

/// Request the window to close. If a close callback is set, it will be invoked
/// first. This must be called from the main thread or via dispatch.
#[no_mangle]
pub extern "C" fn wry_window_close(win: *mut WryWindow) {
    if win.is_null() {
        return;
    }
    let win = unsafe { &mut *win };
    // Trigger a close by destroying the webview and window
    win.webview.take();
    win.window.take();
}

// ---------------------------------------------------------------------------
// Window queries (post-run, via *mut WryWindow from callbacks)
// ---------------------------------------------------------------------------

/// Get the current window size in logical pixels.
#[no_mangle]
pub extern "C" fn wry_window_get_size(
    win: *mut WryWindow,
    width: *mut c_int,
    height: *mut c_int,
) {
    if win.is_null() {
        return;
    }
    let win = unsafe { &*win };
    if let Some(ref w) = win.window {
        let size = w.inner_size();
        let scale = w.scale_factor();
        let logical = size.to_logical::<i32>(scale);
        if !width.is_null() {
            unsafe { *width = logical.width };
        }
        if !height.is_null() {
            unsafe { *height = logical.height };
        }
    }
}

/// Get the current window position in logical pixels.
#[no_mangle]
pub extern "C" fn wry_window_get_position(
    win: *mut WryWindow,
    x: *mut c_int,
    y: *mut c_int,
) {
    if win.is_null() {
        return;
    }
    let win = unsafe { &*win };
    if let Some(ref w) = win.window {
        let pos = w.outer_position().unwrap_or_default();
        let scale = w.scale_factor();
        let logical = pos.to_logical::<i32>(scale);
        if !x.is_null() {
            unsafe { *x = logical.x };
        }
        if !y.is_null() {
            unsafe { *y = logical.y };
        }
    }
}

/// Get the window title. Returns a pointer to a UTF-8 C string that the caller
/// must free with `wry_string_free()`.
#[no_mangle]
pub extern "C" fn wry_window_get_title(win: *mut WryWindow) -> *mut c_char {
    if win.is_null() {
        return std::ptr::null_mut();
    }
    let win = unsafe { &*win };
    let title = if let Some(ref w) = win.window {
        w.title()
    } else {
        String::new()
    };
    CString::new(title)
        .map(|cs| cs.into_raw())
        .unwrap_or(std::ptr::null_mut())
}

/// Free a string returned by `wry_window_get_title` or `wry_window_get_url`.
#[no_mangle]
pub extern "C" fn wry_string_free(s: *mut c_char) {
    if !s.is_null() {
        unsafe {
            drop(CString::from_raw(s));
        }
    }
}

/// Get whether the window is resizable.
#[no_mangle]
pub extern "C" fn wry_window_get_resizable(win: *mut WryWindow) -> bool {
    if win.is_null() {
        return false;
    }
    let win = unsafe { &*win };
    if let Some(ref w) = win.window {
        w.is_resizable()
    } else {
        false
    }
}

/// Get whether the window is fullscreen.
#[no_mangle]
pub extern "C" fn wry_window_get_fullscreen(win: *mut WryWindow) -> bool {
    if win.is_null() {
        return false;
    }
    let win = unsafe { &*win };
    if let Some(ref w) = win.window {
        w.fullscreen().is_some()
    } else {
        false
    }
}

/// Get whether the window is maximized.
#[no_mangle]
pub extern "C" fn wry_window_get_maximized(win: *mut WryWindow) -> bool {
    if win.is_null() {
        return false;
    }
    let win = unsafe { &*win };
    if let Some(ref w) = win.window {
        w.is_maximized()
    } else {
        false
    }
}

/// Get whether the window is minimized.
#[no_mangle]
pub extern "C" fn wry_window_get_minimized(win: *mut WryWindow) -> bool {
    if win.is_null() {
        return false;
    }
    let win = unsafe { &*win };
    if let Some(ref w) = win.window {
        w.is_minimized()
    } else {
        false
    }
}

/// Get whether the window is visible.
#[no_mangle]
pub extern "C" fn wry_window_get_visible(win: *mut WryWindow) -> bool {
    if win.is_null() {
        return false;
    }
    let win = unsafe { &*win };
    if let Some(ref w) = win.window {
        w.is_visible()
    } else {
        false
    }
}

/// Get whether the window has decorations (title bar, borders).
#[no_mangle]
pub extern "C" fn wry_window_get_decorated(win: *mut WryWindow) -> bool {
    if win.is_null() {
        return true;
    }
    let win = unsafe { &*win };
    if let Some(ref w) = win.window {
        w.is_decorated()
    } else {
        true
    }
}

/// Get current window theme. Returns 0 = auto/unknown, 1 = dark, 2 = light.
/// Call from a callback with the WryWindow pointer.
#[no_mangle]
pub extern "C" fn wry_window_get_theme(win: *mut WryWindow) -> c_int {
    if win.is_null() {
        return 0;
    }
    let win = unsafe { &*win };
    if let Some(ref w) = win.window {
        match w.theme() {
            Theme::Dark => 1,
            Theme::Light => 2,
            _ => 0,
        }
    } else {
        0
    }
}

/// Get the DPI scale factor for the window's current monitor.
/// Returns 1.0 as default if the window hasn't been created yet.
#[no_mangle]
pub extern "C" fn wry_window_get_screen_dpi(win: *mut WryWindow) -> f64 {
    if win.is_null() {
        return 1.0;
    }
    let win = unsafe { &*win };
    if let Some(ref w) = win.window {
        w.scale_factor()
    } else {
        1.0
    }
}

/// Get the current URL loaded in the webview. Returns a pointer to a UTF-8
/// C string that the caller must free with `wry_string_free()`.
/// Returns null if the webview is not yet created.
#[no_mangle]
pub extern "C" fn wry_window_get_url(win: *mut WryWindow) -> *mut c_char {
    if win.is_null() {
        return std::ptr::null_mut();
    }
    let win = unsafe { &*win };
    if let Some(ref wv) = win.webview {
        if let Ok(url) = wv.url() {
            return CString::new(url)
                .map(|cs| cs.into_raw())
                .unwrap_or(std::ptr::null_mut());
        }
    }
    std::ptr::null_mut()
}

// ---------------------------------------------------------------------------
// Post-run window property setters (via *mut WryWindow from callbacks)
// ---------------------------------------------------------------------------

/// Set the window title. Call from a callback with the WryWindow pointer.
#[no_mangle]
pub extern "C" fn wry_window_set_title(win: *mut WryWindow, title: *const c_char) {
    if win.is_null() {
        return;
    }
    let win = unsafe { &mut *win };
    let title = unsafe { c_str_to_string(title) };
    if let Some(ref w) = win.window {
        w.set_title(&title);
    }
}

/// Navigate to a URL. Call from a callback with the WryWindow pointer.
#[no_mangle]
pub extern "C" fn wry_window_load_url(win: *mut WryWindow, url: *const c_char) {
    if win.is_null() {
        return;
    }
    let win = unsafe { &mut *win };
    let url = unsafe { c_str_to_string(url) };
    if let Some(ref wv) = win.webview {
        log_err!(wv.load_url(&url), "load_url");
    }
}

/// Load HTML content. Call from a callback with the WryWindow pointer.
#[no_mangle]
pub extern "C" fn wry_window_load_html(win: *mut WryWindow, html: *const c_char) {
    if win.is_null() {
        return;
    }
    let win = unsafe { &mut *win };
    let html = unsafe { c_str_to_string(html) };
    if let Some(ref wv) = win.webview {
        log_err!(wv.load_html(&html), "load_html");
    }
}

/// Set window size. Call from a callback with the WryWindow pointer.
#[no_mangle]
pub extern "C" fn wry_window_set_size(
    win: *mut WryWindow,
    width: c_int,
    height: c_int,
) {
    if win.is_null() {
        return;
    }
    let win = unsafe { &mut *win };
    let w = width.max(1) as u32;
    let h = height.max(1) as u32;
    if let Some(ref window) = win.window {
        window.set_inner_size(LogicalSize::new(w, h));
    }
}

/// Set window position. Call from a callback with the WryWindow pointer.
#[no_mangle]
pub extern "C" fn wry_window_set_position(
    win: *mut WryWindow,
    x: c_int,
    y: c_int,
) {
    if win.is_null() {
        return;
    }
    let win = unsafe { &mut *win };
    if let Some(ref window) = win.window {
        window.set_outer_position(LogicalPosition::new(x, y));
    }
}

/// Set minimum window inner size. Pass width 0 or height 0 to clear the constraint.
/// Call from a callback with the WryWindow pointer.
#[no_mangle]
pub extern "C" fn wry_window_set_min_size(win: *mut WryWindow, width: c_int, height: c_int) {
    if win.is_null() {
        return;
    }
    let win = unsafe { &*win };
    if let Some(ref window) = win.window {
        if width <= 0 || height <= 0 {
            window.set_min_inner_size::<LogicalSize<u32>>(None);
        } else {
            window.set_min_inner_size(Some(LogicalSize::new(width as u32, height as u32)));
        }
    }
}

/// Set maximum window inner size. Pass width 0 or height 0 to clear the constraint.
/// Call from a callback with the WryWindow pointer.
#[no_mangle]
pub extern "C" fn wry_window_set_max_size(win: *mut WryWindow, width: c_int, height: c_int) {
    if win.is_null() {
        return;
    }
    let win = unsafe { &*win };
    if let Some(ref window) = win.window {
        if width <= 0 || height <= 0 {
            window.set_max_inner_size::<LogicalSize<u32>>(None);
        } else {
            window.set_max_inner_size(Some(LogicalSize::new(width as u32, height as u32)));
        }
    }
}

/// Set window theme. theme: 0 = auto/system, 1 = dark, 2 = light.
/// Call from a callback with the WryWindow pointer.
/// Platform: Windows, Linux, macOS (behavior may be app-wide on some platforms).
#[no_mangle]
pub extern "C" fn wry_window_set_theme(win: *mut WryWindow, theme: c_int) {
    if win.is_null() {
        return;
    }
    let win = unsafe { &*win };
    if let Some(ref window) = win.window {
        let t = match theme {
            1 => Some(Theme::Dark),
            2 => Some(Theme::Light),
            _ => None,
        };
        window.set_theme(t);
    }
}

/// Set window decorations. Call from a callback with the WryWindow pointer.
#[no_mangle]
pub extern "C" fn wry_window_set_decorations(win: *mut WryWindow, decorations: bool) {
    if win.is_null() {
        return;
    }
    let win = unsafe { &mut *win };
    if let Some(ref w) = win.window {
        w.set_decorations(decorations);
    }
}

/// Set skip taskbar. Call from a callback with the WryWindow pointer. Platform: Windows, Linux.
#[no_mangle]
pub extern "C" fn wry_window_set_skip_taskbar(win: *mut WryWindow, skip: bool) {
    if win.is_null() {
        return;
    }
    let win = unsafe { &mut *win };
    #[cfg(any(target_os = "windows", target_os = "linux"))]
    if let Some(ref w) = win.window {
        #[cfg(target_os = "windows")]
        {
            use tao::platform::windows::WindowExtWindows;
            let _ = w.set_skip_taskbar(skip);
        }
        #[cfg(target_os = "linux")]
        {
            use tao::platform::unix::WindowExtUnix;
            let _ = w.set_skip_taskbar(skip);
        }
    }
}

/// Set content protection. Call from a callback with the WryWindow pointer.
#[no_mangle]
pub extern "C" fn wry_window_set_content_protected(win: *mut WryWindow, protected: bool) {
    if win.is_null() {
        return;
    }
    let win = unsafe { &mut *win };
    if let Some(ref w) = win.window {
        w.set_content_protection(protected);
    }
}

/// Set undecorated shadow. Call from a callback with the WryWindow pointer. Platform: Windows.
#[no_mangle]
pub extern "C" fn wry_window_set_shadow(win: *mut WryWindow, shadow: bool) {
    if win.is_null() {
        return;
    }
    let win = unsafe { &mut *win };
    #[cfg(target_os = "windows")]
    if let Some(ref w) = win.window {
        use tao::platform::windows::WindowExtWindows;
        w.set_undecorated_shadow(shadow);
    }
}

/// Set always on bottom. Call from a callback with the WryWindow pointer.
#[no_mangle]
pub extern "C" fn wry_window_set_always_on_bottom(win: *mut WryWindow, always_on_bottom: bool) {
    if win.is_null() {
        return;
    }
    let win = unsafe { &mut *win };
    if let Some(ref w) = win.window {
        w.set_always_on_bottom(always_on_bottom);
    }
}

/// Set maximizable. Call from a callback with the WryWindow pointer.
#[no_mangle]
pub extern "C" fn wry_window_set_maximizable(win: *mut WryWindow, maximizable: bool) {
    if win.is_null() {
        return;
    }
    let win = unsafe { &mut *win };
    if let Some(ref w) = win.window {
        w.set_maximizable(maximizable);
    }
}

/// Set minimizable. Call from a callback with the WryWindow pointer.
#[no_mangle]
pub extern "C" fn wry_window_set_minimizable(win: *mut WryWindow, minimizable: bool) {
    if win.is_null() {
        return;
    }
    let win = unsafe { &mut *win };
    if let Some(ref w) = win.window {
        w.set_minimizable(minimizable);
    }
}

/// Set closable. Call from a callback with the WryWindow pointer.
#[no_mangle]
pub extern "C" fn wry_window_set_closable(win: *mut WryWindow, closable: bool) {
    if win.is_null() {
        return;
    }
    let win = unsafe { &mut *win };
    if let Some(ref w) = win.window {
        w.set_closable(closable);
    }
}

/// Set focusable. Call from a callback with the WryWindow pointer.
#[no_mangle]
pub extern "C" fn wry_window_set_focusable(win: *mut WryWindow, focusable: bool) {
    if win.is_null() {
        return;
    }
    let win = unsafe { &mut *win };
    if let Some(ref w) = win.window {
        w.set_focusable(focusable);
    }
}

/// Set webview zoom level. Call from a callback with the WryWindow pointer.
/// 1.0 = 100%, 2.0 = 200%, etc.
#[no_mangle]
pub extern "C" fn wry_window_set_zoom(win: *mut WryWindow, zoom: f64) {
    if win.is_null() {
        return;
    }
    let win = unsafe { &mut *win };
    let z = if zoom > 0.0 { zoom } else { 1.0 };
    if let Some(ref wv) = win.webview {
        log_err!(wv.zoom(z), "zoom");
    }
}

/// Restore the window from minimized or maximized state.
/// Call from a callback with the WryWindow pointer.
#[no_mangle]
pub extern "C" fn wry_window_restore(win: *mut WryWindow) {
    if win.is_null() {
        return;
    }
    let win = unsafe { &mut *win };
    if let Some(ref w) = win.window {
        w.set_minimized(false);
        w.set_maximized(false);
    }
}

/// Set fullscreen state. Call from a callback with the WryWindow pointer.
#[no_mangle]
pub extern "C" fn wry_window_set_fullscreen(win: *mut WryWindow, fullscreen: bool) {
    if win.is_null() {
        return;
    }
    let win = unsafe { &mut *win };
    if let Some(ref w) = win.window {
        if fullscreen {
            w.set_fullscreen(Some(Fullscreen::Borderless(None)));
        } else {
            w.set_fullscreen(None);
        }
    }
}

/// Set maximized state. Call from a callback with the WryWindow pointer.
#[no_mangle]
pub extern "C" fn wry_window_set_maximized(win: *mut WryWindow, maximized: bool) {
    if win.is_null() {
        return;
    }
    let win = unsafe { &mut *win };
    if let Some(ref w) = win.window {
        w.set_maximized(maximized);
    }
}

/// Set minimized state. Call from a callback with the WryWindow pointer.
#[no_mangle]
pub extern "C" fn wry_window_set_minimized(win: *mut WryWindow, minimized: bool) {
    if win.is_null() {
        return;
    }
    let win = unsafe { &mut *win };
    if let Some(ref w) = win.window {
        w.set_minimized(minimized);
    }
}

/// Set topmost (always on top) state. Call from a callback with the WryWindow pointer.
#[no_mangle]
pub extern "C" fn wry_window_set_topmost(win: *mut WryWindow, topmost: bool) {
    if win.is_null() {
        return;
    }
    let win = unsafe { &mut *win };
    if let Some(ref w) = win.window {
        w.set_always_on_top(topmost);
    }
}

/// Set visibility state. Call from a callback with the WryWindow pointer.
#[no_mangle]
pub extern "C" fn wry_window_set_visible(win: *mut WryWindow, visible: bool) {
    if win.is_null() {
        return;
    }
    let win = unsafe { &mut *win };
    if let Some(ref w) = win.window {
        w.set_visible(visible);
    }
}

/// Enumerate all available monitors. The callback is invoked once per monitor
/// with its position (x, y), size (width, height) in physical pixels, and
/// the DPI scale factor. Call from the main thread (from a callback).
#[no_mangle]
pub extern "C" fn wry_window_get_all_monitors(
    win: *mut WryWindow,
    callback: MonitorCallback,
    ctx: *mut c_void,
) {
    if win.is_null() {
        return;
    }
    let win = unsafe { &*win };
    if let Some(ref w) = win.window {
        for monitor in w.available_monitors() {
            let pos = monitor.position();
            let size = monitor.size();
            let scale = monitor.scale_factor();
            callback(
                pos.x as c_int,
                pos.y as c_int,
                size.width as c_int,
                size.height as c_int,
                scale,
                ctx,
            );
        }
    }
}

/// Set resizable state. Call from a callback with the WryWindow pointer.
#[no_mangle]
pub extern "C" fn wry_window_set_resizable(win: *mut WryWindow, resizable: bool) {
    if win.is_null() {
        return;
    }
    let win = unsafe { &mut *win };
    if let Some(ref w) = win.window {
        w.set_resizable(resizable);
    }
}

/// Center the window on its current monitor. Call from a callback with the
/// WryWindow pointer.
#[no_mangle]
pub extern "C" fn wry_window_center(win: *mut WryWindow) {
    if win.is_null() {
        return;
    }
    let win = unsafe { &mut *win };
    if let Some(ref w) = win.window {
        if let Some(monitor) = w.current_monitor() {
            let screen_size = monitor.size();
            let window_size = w.outer_size();
            let x = (screen_size.width as i32 - window_size.width as i32) / 2;
            let y = (screen_size.height as i32 - window_size.height as i32) / 2;
            w.set_outer_position(tao::dpi::PhysicalPosition::new(x.max(0), y.max(0)));
        }
    }
}

// ---------------------------------------------------------------------------
// WebView runtime methods (post-run, via *mut WryWindow from callbacks)
// ---------------------------------------------------------------------------

/// Print the webview content. Call from a callback with the WryWindow pointer.
#[no_mangle]
pub extern "C" fn wry_window_print(win: *mut WryWindow) {
    if win.is_null() {
        return;
    }
    let win = unsafe { &*win };
    if let Some(ref wv) = win.webview {
        log_err!(wv.print(), "print");
    }
}

/// Reload the current page. Call from a callback with the WryWindow pointer.
#[no_mangle]
pub extern "C" fn wry_window_reload(win: *mut WryWindow) {
    if win.is_null() {
        return;
    }
    let win = unsafe { &*win };
    if let Some(ref wv) = win.webview {
        log_err!(wv.reload(), "reload");
    }
}

/// Move focus to the webview. Call from a callback with the WryWindow pointer.
#[no_mangle]
pub extern "C" fn wry_window_focus(win: *mut WryWindow) {
    if win.is_null() {
        return;
    }
    let win = unsafe { &*win };
    if let Some(ref wv) = win.webview {
        log_err!(wv.focus(), "focus");
    }
}

/// Move focus away from the webview back to the parent window.
/// Call from a callback with the WryWindow pointer.
///
/// Platform: Android not implemented.
#[no_mangle]
pub extern "C" fn wry_window_focus_parent(win: *mut WryWindow) {
    if win.is_null() {
        return;
    }
    let win = unsafe { &*win };
    if let Some(ref wv) = win.webview {
        log_err!(wv.focus_parent(), "focus_parent");
    }
}

/// Clear all browsing data. Call from a callback with the WryWindow pointer.
#[no_mangle]
pub extern "C" fn wry_window_clear_all_browsing_data(win: *mut WryWindow) {
    if win.is_null() {
        return;
    }
    let win = unsafe { &*win };
    if let Some(ref wv) = win.webview {
        log_err!(wv.clear_all_browsing_data(), "clear_all_browsing_data");
    }
}

/// Set the webview background color at runtime (RGBA, 0-255 each).
/// Call from a callback with the WryWindow pointer.
///
/// Platform: macOS not implemented.
#[no_mangle]
pub extern "C" fn wry_window_set_background_color(
    win: *mut WryWindow,
    r: u8,
    g: u8,
    b: u8,
    a: u8,
) {
    if win.is_null() {
        return;
    }
    let win = unsafe { &*win };
    if let Some(ref wv) = win.webview {
        log_err!(wv.set_background_color((r, g, b, a)), "set_background_color");
    }
}

/// Set the window icon from RGBA pixel data at runtime.
/// Call from a callback or dispatch with the WryWindow pointer.
/// Pass null / zero length / zero dimensions to clear the icon.
///
/// Platform: Windows and Linux only. macOS has no per-window icon.
#[no_mangle]
pub extern "C" fn wry_window_set_icon(
    win: *mut WryWindow,
    rgba: *const u8,
    rgba_len: c_int,
    width: c_int,
    height: c_int,
) {
    if win.is_null() {
        return;
    }
    let win = unsafe { &*win };
    if let Some(ref w) = win.window {
        if rgba.is_null() || rgba_len <= 0 || width <= 0 || height <= 0 {
            w.set_window_icon(None);
            return;
        }
        let data = unsafe { std::slice::from_raw_parts(rgba, rgba_len as usize) }.to_vec();
        match Icon::from_rgba(data, width as u32, height as u32) {
            Ok(icon) => w.set_window_icon(Some(icon)),
            Err(e) => eprintln!("[wry-native] wry_window_set_icon: {}", e),
        }
    }
}

/// Set the window icon from encoded image file bytes (PNG, ICO, JPEG, BMP, GIF) at runtime.
/// Call from a callback or dispatch with the WryWindow pointer.
/// Pass null or zero length to clear the icon.
///
/// Platform: Windows and Linux only. macOS has no per-window icon.
#[no_mangle]
pub extern "C" fn wry_window_set_icon_from_bytes(
    win: *mut WryWindow,
    data: *const u8,
    data_len: c_int,
) {
    if win.is_null() {
        return;
    }
    let win = unsafe { &*win };
    if let Some(ref w) = win.window {
        if data.is_null() || data_len <= 0 {
            w.set_window_icon(None);
            return;
        }
        let bytes = unsafe { std::slice::from_raw_parts(data, data_len as usize) };
        if let Some(icon) = decode_icon_from_bytes(bytes) {
            w.set_window_icon(Some(icon));
        }
    }
}

/// Open the web inspector (dev tools).
/// Call from a callback with the WryWindow pointer.
///
/// Platform: Android / iOS not supported.
#[no_mangle]
pub extern "C" fn wry_window_open_devtools(win: *mut WryWindow) {
    if win.is_null() {
        return;
    }
    let _win = unsafe { &*win };
    #[cfg(any(debug_assertions, feature = "devtools"))]
    if let Some(ref wv) = _win.webview {
        wv.open_devtools();
    }
}

/// Close the web inspector (dev tools).
/// Call from a callback with the WryWindow pointer.
///
/// Platform: Windows / Android / iOS not supported.
#[no_mangle]
pub extern "C" fn wry_window_close_devtools(win: *mut WryWindow) {
    if win.is_null() {
        return;
    }
    let _win = unsafe { &*win };
    #[cfg(any(debug_assertions, feature = "devtools"))]
    if let Some(ref wv) = _win.webview {
        wv.close_devtools();
    }
}

/// Check if the web inspector (dev tools) is open.
/// Call from a callback with the WryWindow pointer.
/// Returns false if the webview is not created or devtools feature is disabled.
///
/// Platform: Windows / Android / iOS not supported.
#[no_mangle]
pub extern "C" fn wry_window_is_devtools_open(win: *mut WryWindow) -> bool {
    if win.is_null() {
        return false;
    }
    let _win = unsafe { &*win };
    #[cfg(any(debug_assertions, feature = "devtools"))]
    if let Some(ref wv) = _win.webview {
        return wv.is_devtools_open();
    }
    false
}

/// Get the WebView/WebKit engine version on the current platform.
/// Returns a pointer to a UTF-8 C string that the caller must free with
/// `wry_string_free()`. Returns null on failure.
#[no_mangle]
pub extern "C" fn wry_webview_version() -> *mut c_char {
    match webview_version() {
        Ok(version) => CString::new(version)
            .map(|cs| cs.into_raw())
            .unwrap_or(std::ptr::null_mut()),
        Err(_) => std::ptr::null_mut(),
    }
}

// ---------------------------------------------------------------------------
// Cross-thread dispatch
// ---------------------------------------------------------------------------

/// Dispatch a callback to run on the event loop (main) thread. This is safe
/// to call from any thread. The callback will receive the WryWindow pointer
/// and the context pointer.
///
/// `app` is the application handle. `window_id` is the window's numeric ID
/// returned by `wry_window_create`.
#[no_mangle]
pub extern "C" fn wry_window_dispatch(
    app: *mut WryApp,
    window_id: usize,
    callback: DispatchCallback,
    ctx: *mut c_void,
) {
    if app.is_null() {
        return;
    }
    let app = unsafe { &*app };
    log_err!(app.proxy.send_event(UserEvent::Dispatch {
        window_id,
        callback,
        ctx: ctx as usize,
    }), "dispatch");
}

// ---------------------------------------------------------------------------
// Unit tests (pure logic)
// ---------------------------------------------------------------------------

#[cfg(test)]
mod tests {
    use std::ffi::{CStr, CString};

    use super::{c_str_to_string, decode_icon_from_bytes};

    // ---------------------------------------------------------------------------
    // c_str_to_string
    // ---------------------------------------------------------------------------

    #[test]
    fn c_str_to_string_null_returns_empty() {
        let out = unsafe { c_str_to_string(std::ptr::null()) };
        assert_eq!(out, "");
    }

    #[test]
    fn c_str_to_string_valid_utf8_returns_string() {
        let c = CString::new("hello").unwrap();
        let out = unsafe { c_str_to_string(c.as_ptr()) };
        assert_eq!(out, "hello");
    }

    #[test]
    fn c_str_to_string_invalid_utf8_returns_empty() {
        let c = unsafe { CStr::from_bytes_with_nul_unchecked(b"\xff\xfe\0") };
        let out = unsafe { c_str_to_string(c.as_ptr()) };
        assert_eq!(out, "");
    }

    // ---------------------------------------------------------------------------
    // decode_icon_from_bytes
    // ---------------------------------------------------------------------------

    #[test]
    fn decode_icon_from_bytes_empty_returns_none() {
        assert!(decode_icon_from_bytes(&[]).is_none());
    }

    #[test]
    fn decode_icon_from_bytes_invalid_returns_none() {
        assert!(decode_icon_from_bytes(b"not an image").is_none());
    }

    #[test]
    fn decode_icon_from_bytes_valid_png_returns_some() {
        // Minimal 1x1 red pixel PNG (68 bytes)
        const MINIMAL_PNG: &[u8] = &[
            0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 0x00, 0x00, 0x00, 0x0d, 0x49, 0x48,
            0x44, 0x52, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x08, 0x02, 0x00, 0x00,
            0x00, 0x90, 0x77, 0x53, 0xde, 0x00, 0x00, 0x00, 0x0c, 0x49, 0x44, 0x41, 0x54, 0x08,
            0xd7, 0x63, 0xf8, 0xff, 0xff, 0x3f, 0x00, 0x05, 0xfe, 0x02, 0xfe, 0xdc, 0xcc, 0x59,
            0xe7, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4e, 0x44, 0xae, 0x42, 0x60, 0x82,
        ];
        let icon = decode_icon_from_bytes(MINIMAL_PNG);
        assert!(icon.is_some());
    }
}

