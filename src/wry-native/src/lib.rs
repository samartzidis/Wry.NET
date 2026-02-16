//! wry-native: Cross-platform webview C API built on wry + tao.
//!
//! This crate produces a cdylib (.dll / .so / .dylib) that exposes a flat C API
//! for creating native webview windows. It is designed for consumption from C#
//! via P/Invoke, but works from any language with C FFI.
//!
//! # Usage pattern ("Configure, Run, Respond")
//!
//! 1. Call `wry_app_new()` to create the application.
//! 2. Call `wry_window_new(app)` to create a window handle.
//! 3. Configure the window with `wry_window_set_*` calls.
//! 4. Register callbacks with `wry_window_on_*` / `wry_window_set_ipc_handler`.
//! 5. Call `wry_app_run(app)` -- this blocks the main thread.
//! 6. Inside callbacks, call `wry_window_*` methods directly (same thread).
//! 7. From background threads, use `wry_window_dispatch()` to marshal to main.
//! 8. When the last window closes, `wry_app_run()` returns.
//! 9. Call `wry_app_destroy(app)` to free resources.

#![allow(clippy::missing_safety_doc)]

use std::borrow::Cow;
use std::collections::HashMap;
use std::ffi::{c_char, c_int, c_void, CStr, CString};

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
use tao::window::{Fullscreen, Icon, Window, WindowBuilder as TaoWindowBuilder, WindowId};

use wry::{webview_version, WebContext, WebView, WebViewBuilder};

#[cfg(target_os = "windows")]
use wry::WebViewBuilderExtWindows;

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
}

// Safety: the ctx pointer is opaque and only dereferenced by the C caller's
// callback which is responsible for its own thread safety.
unsafe impl Send for UserEvent {}

// ---------------------------------------------------------------------------
// Pending protocol registration
// ---------------------------------------------------------------------------

struct PendingProtocol {
    scheme: String,
    callback: ProtocolHandlerCallback,
    ctx: usize,
}

// ---------------------------------------------------------------------------
// WryWindow -- per-window state
// ---------------------------------------------------------------------------

pub struct WryWindow {
    id: usize,

    // --- Pending config (set before app_run, consumed when window is created) ---
    pending_title: String,
    pending_url: Option<String>,
    pending_html: Option<String>,
    pending_size: (u32, u32),
    pending_min_size: Option<(u32, u32)>,
    pending_max_size: Option<(u32, u32)>,
    pending_position: Option<(i32, i32)>,
    pending_resizable: bool,
    pending_fullscreen: bool,
    pending_maximized: bool,
    pending_minimized: bool,
    pending_topmost: bool,
    pending_visible: bool,
    pending_devtools: bool,
    pending_transparent: bool,
    pending_decorations: bool,
    pending_user_agent: Option<String>,
    pending_zoom: f64,
    pending_back_forward_gestures: bool,
    pending_autoplay: bool,
    pending_hotkeys_zoom: bool,
    pending_clipboard: bool,
    pending_accept_first_mouse: bool,
    pending_incognito: bool,
    pending_focused: bool,
    pending_javascript_disabled: bool,
    pending_background_color: Option<(u8, u8, u8, u8)>,
    pending_background_throttling: Option<i32>, // 0=Disabled, 1=Suspend, 2=Throttle
    #[cfg(target_os = "windows")]
    pending_theme: i32, // 0=Auto, 1=Dark, 2=Light
    #[cfg(target_os = "windows")]
    pending_https_scheme: bool,
    #[cfg(target_os = "windows")]
    pending_browser_accelerator_keys: bool,
    #[cfg(target_os = "windows")]
    pending_default_context_menus: bool,
    #[cfg(target_os = "windows")]
    pending_scroll_bar_style: i32, // 0=Default, 1=FluentOverlay
    pending_init_scripts: Vec<String>,
    pending_protocols: Vec<PendingProtocol>,
    pending_data_directory: Option<String>,
    pending_icon: Option<Icon>,

    // --- Callbacks ---
    ipc_handler: Option<(IpcCallback, usize)>,
    close_handler: Option<(CloseCallback, usize)>,
    resize_handler: Option<(ResizeCallback, usize)>,
    move_handler: Option<(MoveCallback, usize)>,
    focus_handler: Option<(FocusCallback, usize)>,
    navigation_handler: Option<(NavigationCallback, usize)>,
    page_load_handler: Option<(PageLoadCallback, usize)>,
    drag_drop_handler: Option<(DragDropCallback, usize)>,

    // --- Live objects (populated during app_run) ---
    window: Option<Window>,
    webview: Option<WebView>,
    web_context: Option<WebContext>,
    window_id: Option<WindowId>,
}

impl WryWindow {
    fn new(id: usize) -> Self {
        Self {
            id,
            pending_title: String::new(),
            pending_url: None,
            pending_html: None,
            pending_size: (800, 600),
            pending_min_size: None,
            pending_max_size: None,
            pending_position: None,
            pending_resizable: true,
            pending_fullscreen: false,
            pending_maximized: false,
            pending_minimized: false,
            pending_topmost: false,
            pending_visible: true,
            pending_devtools: false,
            pending_transparent: false,
            pending_decorations: true,
            pending_user_agent: None,
            pending_zoom: 1.0,
            pending_back_forward_gestures: false,
            pending_autoplay: false,
            pending_hotkeys_zoom: true,
            pending_clipboard: false,
            pending_accept_first_mouse: false,
            pending_incognito: false,
            pending_focused: true,
            pending_javascript_disabled: false,
            pending_background_color: None,
            pending_background_throttling: None,
            #[cfg(target_os = "windows")]
            pending_theme: 0,
            #[cfg(target_os = "windows")]
            pending_https_scheme: false,
            #[cfg(target_os = "windows")]
            pending_browser_accelerator_keys: true,
            #[cfg(target_os = "windows")]
            pending_default_context_menus: true,
            #[cfg(target_os = "windows")]
            pending_scroll_bar_style: 0,
            pending_init_scripts: Vec::new(),
            pending_protocols: Vec::new(),
            pending_data_directory: None,
            pending_icon: None,
            ipc_handler: None,
            close_handler: None,
            resize_handler: None,
            move_handler: None,
            focus_handler: None,
            navigation_handler: None,
            page_load_handler: None,
            drag_drop_handler: None,
            window: None,
            webview: None,
            web_context: None,
            window_id: None,
        }
    }

    /// Materialize the tao Window + wry WebView from pending config.
    fn create(
        &mut self,
        event_loop: &EventLoopWindowTarget<UserEvent>,
    ) {
        let (w, h) = self.pending_size;
        let mut wb = TaoWindowBuilder::new()
            .with_title(&self.pending_title)
            .with_inner_size(LogicalSize::new(w, h))
            .with_resizable(self.pending_resizable)
            .with_always_on_top(self.pending_topmost)
            .with_visible(self.pending_visible)
            .with_maximized(self.pending_maximized)
            .with_decorations(self.pending_decorations);

        if let Some((min_w, min_h)) = self.pending_min_size {
            wb = wb.with_min_inner_size(LogicalSize::new(min_w, min_h));
        }
        if let Some((max_w, max_h)) = self.pending_max_size {
            wb = wb.with_max_inner_size(LogicalSize::new(max_w, max_h));
        }
        if let Some((x, y)) = self.pending_position {
            wb = wb.with_position(LogicalPosition::new(x, y));
        }
        if self.pending_fullscreen {
            wb = wb.with_fullscreen(Some(Fullscreen::Borderless(None)));
        }
        if let Some(icon) = self.pending_icon.take() {
            wb = wb.with_window_icon(Some(icon));
        }

        let window = wb.build(event_loop).expect("failed to create window");

        // Build webview -- optionally with a WebContext for data directory
        if let Some(ref dir) = self.pending_data_directory {
            self.web_context = Some(WebContext::new(Some(std::path::PathBuf::from(dir))));
        }

        let mut wvb = if let Some(ref mut ctx) = self.web_context {
            WebViewBuilder::new_with_web_context(ctx)
        } else {
            WebViewBuilder::new()
        };

        // URL or HTML
        if let Some(ref url) = self.pending_url {
            wvb = wvb.with_url(url);
        } else if let Some(ref html) = self.pending_html {
            wvb = wvb.with_html(html);
        }

        // User agent
        if let Some(ref ua) = self.pending_user_agent {
            wvb = wvb.with_user_agent(ua);
        }

        // Transparent
        if self.pending_transparent {
            wvb = wvb.with_transparent(true);
        }

        // Background color
        if let Some((r, g, b, a)) = self.pending_background_color {
            wvb = wvb.with_background_color((r, g, b, a));
        }

        // Devtools
        #[cfg(any(debug_assertions, feature = "devtools"))]
        {
            wvb = wvb.with_devtools(self.pending_devtools);
        }
        let _ = self.pending_devtools; // suppress unused warning in release

        // Simple bool builder flags
        wvb = wvb.with_back_forward_navigation_gestures(self.pending_back_forward_gestures);
        wvb = wvb.with_autoplay(self.pending_autoplay);
        wvb = wvb.with_hotkeys_zoom(self.pending_hotkeys_zoom);
        wvb = wvb.with_clipboard(self.pending_clipboard);
        wvb = wvb.with_accept_first_mouse(self.pending_accept_first_mouse);
        wvb = wvb.with_incognito(self.pending_incognito);
        wvb = wvb.with_focused(self.pending_focused);

        if self.pending_javascript_disabled {
            wvb = wvb.with_javascript_disabled();
        }

        // Background throttling
        if let Some(policy) = self.pending_background_throttling {
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
            let theme = match self.pending_theme {
                1 => Theme::Dark,
                2 => Theme::Light,
                _ => Theme::Auto,
            };
            wvb = wvb.with_theme(theme);
            wvb = wvb.with_https_scheme(self.pending_https_scheme);
            wvb = wvb.with_browser_accelerator_keys(self.pending_browser_accelerator_keys);
            wvb = wvb.with_default_context_menus(self.pending_default_context_menus);
            let style = match self.pending_scroll_bar_style {
                1 => ScrollBarStyle::FluentOverlay,
                _ => ScrollBarStyle::Default,
            };
            wvb = wvb.with_scroll_bar_style(style);
        }

        // Init scripts
        for script in &self.pending_init_scripts {
            wvb = wvb.with_initialization_script(script);
        }

        // IPC handler
        if let Some((cb, ctx)) = self.ipc_handler {
            wvb = wvb.with_ipc_handler(move |req| {
                let url = req.uri().to_string();
                let body = req.body();
                if let (Ok(c_body), Ok(c_url)) = (CString::new(body.as_str()), CString::new(url)) {
                    cb(c_body.as_ptr(), c_url.as_ptr(), ctx as *mut c_void);
                }
            });
        }

        // Navigation handler
        if let Some((cb, ctx)) = self.navigation_handler {
            wvb = wvb.with_navigation_handler(move |url| {
                if let Ok(c_url) = CString::new(url.as_str()) {
                    cb(c_url.as_ptr(), ctx as *mut c_void)
                } else {
                    true // allow on encoding error
                }
            });
        }

        // Page load handler
        if let Some((cb, ctx)) = self.page_load_handler {
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

        // Drag-drop handler
        if let Some((cb, ctx)) = self.drag_drop_handler {
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

        // Custom protocols
        for proto in self.pending_protocols.drain(..) {
            let cb = proto.callback;
            let ctx = proto.ctx;
            wvb = wvb.with_asynchronous_custom_protocol(proto.scheme, move |_id, request, responder| {
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
            .expect("failed to create webview");

        // Apply zoom if not default
        if (self.pending_zoom - 1.0).abs() > f64::EPSILON {
            log_err!(webview.zoom(self.pending_zoom), "zoom (init)");
        }

        self.window_id = Some(window.id());
        self.window = Some(window);
        self.webview = Some(webview);

        // Apply post-creation state
        if self.pending_minimized {
            if let Some(ref w) = self.window {
                w.set_minimized(true);
            }
        }
    }
}

// ---------------------------------------------------------------------------
// WryApp -- application-level state
// ---------------------------------------------------------------------------

pub struct WryApp {
    event_loop: Option<EventLoop<UserEvent>>,
    pub(crate) proxy: EventLoopProxy<UserEvent>,
    windows: HashMap<usize, WryWindow>,
    next_window_id: usize,
    pub(crate) trays: HashMap<usize, WryTray>,
    pub(crate) next_tray_id: usize,
    exit_requested_handler: Option<(ExitRequestedCallback, usize)>,
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
        next_window_id: 1,
        trays: HashMap::new(),
        next_tray_id: 1,
        exit_requested_handler: None,
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

    // Move windows out of the app struct so they can be owned by the closure.
    // We use a separate map keyed by tao WindowId for event dispatch.
    let mut pending_windows: Vec<WryWindow> = app.windows.drain().map(|(_, w)| w).collect();
    let mut live_windows: HashMap<WindowId, WryWindow> = HashMap::new();
    // Also keep a map from our usize id -> WindowId for dispatch lookups.
    let mut id_to_window_id: HashMap<usize, WindowId> = HashMap::new();

    // Move trays out of the app struct.
    let mut pending_trays: Vec<WryTray> = app.trays.drain().map(|(_, t)| t).collect();
    let mut live_trays: HashMap<usize, WryTray> = HashMap::new();
    // Map from menu item string ID to tray usize ID for event routing.
    let mut menu_id_to_tray: HashMap<String, usize> = HashMap::new();

    // Exit-requested callback (fired when all windows are closed).
    let exit_requested_handler = app.exit_requested_handler.take();

    // Wire up tray icon / menu event handlers to forward into the event loop.
    tray::setup_tray_event_handlers(&app.proxy);

    // Use run_return so we return to the caller instead of calling process::exit.
    event_loop.run_return(move |event, event_loop_target, control_flow| {
        *control_flow = ControlFlow::Wait;

        match event {
            Event::NewEvents(StartCause::Init) => {
                // Materialize all pending windows.
                for mut win in pending_windows.drain(..) {
                    win.create(event_loop_target);
                    if let Some(wid) = win.window_id {
                        let our_id = win.id;
                        id_to_window_id.insert(our_id, wid);
                        live_windows.insert(wid, win);
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

/// Create a new window handle. The window is not visible until `wry_app_run()`
/// is called. Returns an opaque window ID (not a pointer) that is used in
/// subsequent calls. Returns 0 on failure.
#[no_mangle]
pub extern "C" fn wry_window_new(app: *mut WryApp) -> usize {
    if app.is_null() {
        return 0;
    }
    let app = unsafe { &mut *app };
    let id = app.next_window_id;
    app.next_window_id += 1;
    let win = WryWindow::new(id);
    app.windows.insert(id, win);
    id
}

// ---------------------------------------------------------------------------
// Helpers: look up a WryWindow by ID from the app (pre-run only).
// During run, the windows are moved into the event loop closure, so callers
// from callbacks receive a *mut WryWindow directly.
// ---------------------------------------------------------------------------

/// Get a mutable reference to a window by ID from the app.
/// Only valid before `wry_app_run()`. Returns null if not found.
fn get_pending_window(app: *mut WryApp, window_id: usize) -> Option<&'static mut WryWindow> {
    if app.is_null() {
        return None;
    }
    let app = unsafe { &mut *app };
    app.windows.get_mut(&window_id).map(|w| {
        // Safety: the pointer is valid for the lifetime of the app, and we're
        // single-threaded before run(). We use 'static as a convenience -- the
        // actual lifetime is bounded by app_run consuming the windows.
        unsafe { &mut *(w as *mut WryWindow) }
    })
}

// ---------------------------------------------------------------------------
// Navigation & JS interop (pre-run: via app+id, post-run: via *mut WryWindow)
// ---------------------------------------------------------------------------

/// Set the URL to load. Call before `wry_app_run()`.
#[no_mangle]
pub extern "C" fn wry_window_load_url(app: *mut WryApp, window_id: usize, url: *const c_char) {
    if let Some(win) = get_pending_window(app, window_id) {
        let url = unsafe { c_str_to_string(url) };
        if win.webview.is_some() {
            // Post-creation: navigate directly
            if let Some(ref wv) = win.webview {
                log_err!(wv.load_url(&url), "load_url");
            }
        } else {
            win.pending_url = Some(url);
            win.pending_html = None;
        }
    }
}

/// Set HTML content to load. Call before `wry_app_run()`.
#[no_mangle]
pub extern "C" fn wry_window_load_html(app: *mut WryApp, window_id: usize, html: *const c_char) {
    if let Some(win) = get_pending_window(app, window_id) {
        let html = unsafe { c_str_to_string(html) };
        if win.webview.is_some() {
            if let Some(ref wv) = win.webview {
                log_err!(wv.load_html(&html), "load_html");
            }
        } else {
            win.pending_html = Some(html);
            win.pending_url = None;
        }
    }
}

/// Evaluate JavaScript in the webview. When called before `wry_app_run()`, the
/// script is queued as an init script. When called from a callback (post-run),
/// pass the `*mut WryWindow` pointer directly.
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

/// Add an initialization script that runs before page load.
/// Must be called before `wry_app_run()`.
#[no_mangle]
pub extern "C" fn wry_window_add_init_script(
    app: *mut WryApp,
    window_id: usize,
    js: *const c_char,
) {
    if let Some(win) = get_pending_window(app, window_id) {
        let js = unsafe { c_str_to_string(js) };
        if !js.is_empty() {
            win.pending_init_scripts.push(js);
        }
    }
}

/// Set the IPC message handler. Must be called before `wry_app_run()`.
/// The callback receives the message body and the origin URL as UTF-8 strings.
#[no_mangle]
pub extern "C" fn wry_window_set_ipc_handler(
    app: *mut WryApp,
    window_id: usize,
    callback: IpcCallback,
    ctx: *mut c_void,
) {
    if let Some(win) = get_pending_window(app, window_id) {
        win.ipc_handler = Some((callback, ctx as usize));
    }
}

/// Register a custom protocol handler. Must be called before `wry_app_run()`.
///
/// When the webview navigates to `{scheme}://...`, the callback is invoked with
/// the full URL and a responder handle. The callback MUST call
/// `wry_protocol_respond()` with the responder to deliver the response.
#[no_mangle]
pub extern "C" fn wry_window_add_custom_protocol(
    app: *mut WryApp,
    window_id: usize,
    scheme: *const c_char,
    callback: ProtocolHandlerCallback,
    ctx: *mut c_void,
) {
    if let Some(win) = get_pending_window(app, window_id) {
        let scheme = unsafe { c_str_to_string(scheme) };
        if !scheme.is_empty() {
            win.pending_protocols.push(PendingProtocol {
                scheme,
                callback,
                ctx: ctx as usize,
            });
        }
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
// Window property setters (pre-run via app+id)
// ---------------------------------------------------------------------------

#[no_mangle]
pub extern "C" fn wry_window_set_title(
    app: *mut WryApp,
    window_id: usize,
    title: *const c_char,
) {
    if let Some(win) = get_pending_window(app, window_id) {
        let title = unsafe { c_str_to_string(title) };
        if let Some(ref w) = win.window {
            w.set_title(&title);
        }
        win.pending_title = title;
    }
}

#[no_mangle]
pub extern "C" fn wry_window_set_size(
    app: *mut WryApp,
    window_id: usize,
    width: c_int,
    height: c_int,
) {
    if let Some(win) = get_pending_window(app, window_id) {
        let w = width.max(1) as u32;
        let h = height.max(1) as u32;
        if let Some(ref window) = win.window {
            window.set_inner_size(LogicalSize::new(w, h));
        }
        win.pending_size = (w, h);
    }
}

#[no_mangle]
pub extern "C" fn wry_window_set_min_size(
    app: *mut WryApp,
    window_id: usize,
    width: c_int,
    height: c_int,
) {
    if let Some(win) = get_pending_window(app, window_id) {
        let w = width.max(0) as u32;
        let h = height.max(0) as u32;
        if w == 0 && h == 0 {
            win.pending_min_size = None;
            if let Some(ref window) = win.window {
                window.set_min_inner_size::<LogicalSize<u32>>(None);
            }
        } else {
            win.pending_min_size = Some((w, h));
            if let Some(ref window) = win.window {
                window.set_min_inner_size(Some(LogicalSize::new(w, h)));
            }
        }
    }
}

#[no_mangle]
pub extern "C" fn wry_window_set_max_size(
    app: *mut WryApp,
    window_id: usize,
    width: c_int,
    height: c_int,
) {
    if let Some(win) = get_pending_window(app, window_id) {
        let w = width.max(0) as u32;
        let h = height.max(0) as u32;
        if w == 0 && h == 0 {
            win.pending_max_size = None;
            if let Some(ref window) = win.window {
                window.set_max_inner_size::<LogicalSize<u32>>(None);
            }
        } else {
            win.pending_max_size = Some((w, h));
            if let Some(ref window) = win.window {
                window.set_max_inner_size(Some(LogicalSize::new(w, h)));
            }
        }
    }
}

#[no_mangle]
pub extern "C" fn wry_window_set_position(
    app: *mut WryApp,
    window_id: usize,
    x: c_int,
    y: c_int,
) {
    if let Some(win) = get_pending_window(app, window_id) {
        if let Some(ref w) = win.window {
            w.set_outer_position(LogicalPosition::new(x, y));
        }
        win.pending_position = Some((x, y));
    }
}

#[no_mangle]
pub extern "C" fn wry_window_set_resizable(
    app: *mut WryApp,
    window_id: usize,
    resizable: bool,
) {
    if let Some(win) = get_pending_window(app, window_id) {
        if let Some(ref w) = win.window {
            w.set_resizable(resizable);
        }
        win.pending_resizable = resizable;
    }
}

#[no_mangle]
pub extern "C" fn wry_window_set_fullscreen(
    app: *mut WryApp,
    window_id: usize,
    fullscreen: bool,
) {
    if let Some(win) = get_pending_window(app, window_id) {
        if let Some(ref w) = win.window {
            if fullscreen {
                w.set_fullscreen(Some(Fullscreen::Borderless(None)));
            } else {
                w.set_fullscreen(None);
            }
        }
        win.pending_fullscreen = fullscreen;
    }
}

#[no_mangle]
pub extern "C" fn wry_window_set_maximized(
    app: *mut WryApp,
    window_id: usize,
    maximized: bool,
) {
    if let Some(win) = get_pending_window(app, window_id) {
        if let Some(ref w) = win.window {
            w.set_maximized(maximized);
        }
        win.pending_maximized = maximized;
    }
}

#[no_mangle]
pub extern "C" fn wry_window_set_minimized(
    app: *mut WryApp,
    window_id: usize,
    minimized: bool,
) {
    if let Some(win) = get_pending_window(app, window_id) {
        if let Some(ref w) = win.window {
            w.set_minimized(minimized);
        }
        win.pending_minimized = minimized;
    }
}

#[no_mangle]
pub extern "C" fn wry_window_set_topmost(
    app: *mut WryApp,
    window_id: usize,
    topmost: bool,
) {
    if let Some(win) = get_pending_window(app, window_id) {
        if let Some(ref w) = win.window {
            w.set_always_on_top(topmost);
        }
        win.pending_topmost = topmost;
    }
}

#[no_mangle]
pub extern "C" fn wry_window_set_visible(
    app: *mut WryApp,
    window_id: usize,
    visible: bool,
) {
    if let Some(win) = get_pending_window(app, window_id) {
        if let Some(ref w) = win.window {
            w.set_visible(visible);
        }
        win.pending_visible = visible;
    }
}

#[no_mangle]
pub extern "C" fn wry_window_set_devtools(
    app: *mut WryApp,
    window_id: usize,
    enabled: bool,
) {
    if let Some(win) = get_pending_window(app, window_id) {
        win.pending_devtools = enabled;
        // If already created, open/close devtools
        #[cfg(any(debug_assertions, feature = "devtools"))]
        if let Some(ref _wv) = win.webview {
            if enabled {
                _wv.open_devtools();
            } else {
                _wv.close_devtools();
            }
        }
    }
}

#[no_mangle]
pub extern "C" fn wry_window_set_transparent(
    app: *mut WryApp,
    window_id: usize,
    transparent: bool,
) {
    if let Some(win) = get_pending_window(app, window_id) {
        // Transparency must be set before webview creation
        win.pending_transparent = transparent;
    }
}

/// Set whether the window has decorations (title bar, borders).
/// `false` creates a "chromeless" window.
#[no_mangle]
pub extern "C" fn wry_window_set_decorations(
    app: *mut WryApp,
    window_id: usize,
    decorations: bool,
) {
    if let Some(win) = get_pending_window(app, window_id) {
        if let Some(ref w) = win.window {
            w.set_decorations(decorations);
        }
        win.pending_decorations = decorations;
    }
}

/// Set a custom user agent string for the webview.
/// Must be called before `wry_app_run()` (cannot be changed at runtime).
#[no_mangle]
pub extern "C" fn wry_window_set_user_agent(
    app: *mut WryApp,
    window_id: usize,
    user_agent: *const c_char,
) {
    if let Some(win) = get_pending_window(app, window_id) {
        let ua = unsafe { c_str_to_string(user_agent) };
        if ua.is_empty() {
            win.pending_user_agent = None;
        } else {
            win.pending_user_agent = Some(ua);
        }
    }
}

/// Set the webview zoom level. 1.0 = 100%, 2.0 = 200%, etc.
/// Before `wry_app_run()`, sets the initial zoom. From callbacks, use
/// `wry_window_set_zoom_direct`.
#[no_mangle]
pub extern "C" fn wry_window_set_zoom(
    app: *mut WryApp,
    window_id: usize,
    zoom: f64,
) {
    if let Some(win) = get_pending_window(app, window_id) {
        let z = if zoom > 0.0 { zoom } else { 1.0 };
        if let Some(ref wv) = win.webview {
            log_err!(wv.zoom(z), "zoom (pre-run)");
        }
        win.pending_zoom = z;
    }
}

/// Enable/disable backward and forward navigation gestures (horizontal swipe).
/// Must be called before `wry_app_run()`.
///
/// Platform: Android / iOS unsupported.
#[no_mangle]
pub extern "C" fn wry_window_set_back_forward_gestures(
    app: *mut WryApp,
    window_id: usize,
    enabled: bool,
) {
    if let Some(win) = get_pending_window(app, window_id) {
        win.pending_back_forward_gestures = enabled;
    }
}

/// Enable/disable autoplay of all media without user interaction.
/// Must be called before `wry_app_run()`.
#[no_mangle]
pub extern "C" fn wry_window_set_autoplay(
    app: *mut WryApp,
    window_id: usize,
    enabled: bool,
) {
    if let Some(win) = get_pending_window(app, window_id) {
        win.pending_autoplay = enabled;
    }
}

/// Enable/disable page zooming via hotkeys or gestures.
/// Must be called before `wry_app_run()`. Default is true.
///
/// Platform: macOS / Linux / Android / iOS unsupported.
#[no_mangle]
pub extern "C" fn wry_window_set_hotkeys_zoom(
    app: *mut WryApp,
    window_id: usize,
    enabled: bool,
) {
    if let Some(win) = get_pending_window(app, window_id) {
        win.pending_hotkeys_zoom = enabled;
    }
}

/// Enable/disable clipboard access for the page (Linux and Windows).
/// macOS is always enabled.
/// Must be called before `wry_app_run()`.
#[no_mangle]
pub extern "C" fn wry_window_set_clipboard(
    app: *mut WryApp,
    window_id: usize,
    enabled: bool,
) {
    if let Some(win) = get_pending_window(app, window_id) {
        win.pending_clipboard = enabled;
    }
}

/// Set whether clicking an inactive window also clicks through to the webview.
/// Must be called before `wry_app_run()`. Default is false.
///
/// Platform: macOS only.
#[no_mangle]
pub extern "C" fn wry_window_set_accept_first_mouse(
    app: *mut WryApp,
    window_id: usize,
    enabled: bool,
) {
    if let Some(win) = get_pending_window(app, window_id) {
        win.pending_accept_first_mouse = enabled;
    }
}

/// Enable/disable incognito (private browsing) mode.
/// Must be called before `wry_app_run()`.
///
/// Platform: Android unsupported.
#[no_mangle]
pub extern "C" fn wry_window_set_incognito(
    app: *mut WryApp,
    window_id: usize,
    enabled: bool,
) {
    if let Some(win) = get_pending_window(app, window_id) {
        win.pending_incognito = enabled;
    }
}

/// Set the data directory for the webview's user data (cache, cookies, etc.).
/// Must be called before `wry_app_run()`.
///
/// If not set, the data directory defaults to the directory of the executable,
/// which is inappropriate for installed apps (e.g. Program Files).
/// Recommended: pass a path under `%LOCALAPPDATA%/<AppName>`.
///
/// Platform: Windows (WebView2 user data folder). On macOS/Linux this is
/// handled by the underlying WebContext.
#[no_mangle]
pub extern "C" fn wry_window_set_data_directory(
    app: *mut WryApp,
    window_id: usize,
    path: *const c_char,
) {
    if let Some(win) = get_pending_window(app, window_id) {
        if !path.is_null() {
            let s = unsafe { CStr::from_ptr(path) }.to_string_lossy().into_owned();
            win.pending_data_directory = Some(s);
        }
    }
}

/// Set the window icon from RGBA pixel data.
/// Must be called before `wry_app_run()` for the initial icon.
///
/// - `rgba`: pointer to RGBA pixel data (4 bytes per pixel, row-major)
/// - `rgba_len`: length of the data in bytes (must equal width * height * 4)
/// - `width`: icon width in pixels
/// - `height`: icon height in pixels
///
/// Platform: Windows and Linux only. macOS uses the .app bundle icon.
#[no_mangle]
pub extern "C" fn wry_window_set_icon(
    app: *mut WryApp,
    window_id: usize,
    rgba: *const u8,
    rgba_len: c_int,
    width: c_int,
    height: c_int,
) {
    if let Some(win) = get_pending_window(app, window_id) {
        if rgba.is_null() || rgba_len <= 0 || width <= 0 || height <= 0 {
            win.pending_icon = None;
            return;
        }
        let data = unsafe { std::slice::from_raw_parts(rgba, rgba_len as usize) }.to_vec();
        match Icon::from_rgba(data, width as u32, height as u32) {
            Ok(icon) => {
                win.pending_icon = Some(icon);
            }
            Err(e) => {
                eprintln!("[wry-native] set_icon failed: {}", e);
            }
        }
    }
}

/// Set the window icon from RGBA pixel data at runtime.
/// Call from a callback or dispatch with the WryWindow pointer.
///
/// Platform: Windows and Linux only. macOS uses the .app bundle icon.
#[no_mangle]
pub extern "C" fn wry_window_set_icon_direct(
    win: *mut WryWindow,
    rgba: *const u8,
    rgba_len: c_int,
    width: c_int,
    height: c_int,
) {
    if win.is_null() {
        return;
    }
    let win = unsafe { &mut *win };
    if rgba.is_null() || rgba_len <= 0 || width <= 0 || height <= 0 {
        if let Some(ref w) = win.window {
            w.set_window_icon(None);
        }
        return;
    }
    let data = unsafe { std::slice::from_raw_parts(rgba, rgba_len as usize) }.to_vec();
    match Icon::from_rgba(data, width as u32, height as u32) {
        Ok(icon) => {
            if let Some(ref w) = win.window {
                w.set_window_icon(Some(icon));
            }
        }
        Err(e) => {
            eprintln!("[wry-native] set_icon_direct failed: {}", e);
        }
    }
}

/// Helper: decode image file bytes (PNG, ICO, JPEG, BMP, GIF) into an RGBA Icon.
fn decode_icon_from_bytes(data: &[u8]) -> Option<Icon> {
    use image::GenericImageView;
    match image::load_from_memory(data) {
        Ok(img) => {
            let rgba = img.to_rgba8();
            let (w, h) = img.dimensions();
            match Icon::from_rgba(rgba.into_raw(), w, h) {
                Ok(icon) => Some(icon),
                Err(e) => {
                    eprintln!("[wry-native] Icon::from_rgba failed: {}", e);
                    None
                }
            }
        }
        Err(e) => {
            eprintln!("[wry-native] image decode failed: {}", e);
            None
        }
    }
}

/// Set the window icon from encoded image file bytes (PNG, ICO, JPEG, BMP, GIF).
/// The image is decoded on the Rust side — no image library needed in the caller.
/// Must be called before `wry_app_run()`.
///
/// - `data`: pointer to the image file bytes (e.g. contents of a .png file)
/// - `data_len`: length of the data in bytes
///
/// Platform: Windows and Linux only. macOS uses the .app bundle icon.
#[no_mangle]
pub extern "C" fn wry_window_set_icon_from_bytes(
    app: *mut WryApp,
    window_id: usize,
    data: *const u8,
    data_len: c_int,
) {
    if let Some(win) = get_pending_window(app, window_id) {
        if data.is_null() || data_len <= 0 {
            win.pending_icon = None;
            return;
        }
        let bytes = unsafe { std::slice::from_raw_parts(data, data_len as usize) };
        win.pending_icon = decode_icon_from_bytes(bytes);
    }
}

/// Set the window icon from encoded image file bytes at runtime.
/// Call from a callback or dispatch with the WryWindow pointer.
///
/// Platform: Windows and Linux only. macOS uses the .app bundle icon.
#[no_mangle]
pub extern "C" fn wry_window_set_icon_from_bytes_direct(
    win: *mut WryWindow,
    data: *const u8,
    data_len: c_int,
) {
    if win.is_null() {
        return;
    }
    let win = unsafe { &mut *win };
    if data.is_null() || data_len <= 0 {
        if let Some(ref w) = win.window {
            w.set_window_icon(None);
        }
        return;
    }
    let bytes = unsafe { std::slice::from_raw_parts(data, data_len as usize) };
    if let Some(icon) = decode_icon_from_bytes(bytes) {
        if let Some(ref w) = win.window {
            w.set_window_icon(Some(icon));
        }
    }
}

/// Set whether the webview should be focused when created.
/// Must be called before `wry_app_run()`. Default is true.
///
/// Platform: macOS / Android / iOS unsupported.
#[no_mangle]
pub extern "C" fn wry_window_set_focused(
    app: *mut WryApp,
    window_id: usize,
    focused: bool,
) {
    if let Some(win) = get_pending_window(app, window_id) {
        win.pending_focused = focused;
    }
}

/// Disable JavaScript in the webview.
/// Must be called before `wry_app_run()`. Default is false (JS enabled).
#[no_mangle]
pub extern "C" fn wry_window_set_javascript_disabled(
    app: *mut WryApp,
    window_id: usize,
    disabled: bool,
) {
    if let Some(win) = get_pending_window(app, window_id) {
        win.pending_javascript_disabled = disabled;
    }
}

/// Set the webview background color (RGBA, 0-255 each).
/// Ignored if transparent is set to true.
/// Must be called before `wry_app_run()` for initial value.
///
/// Platform: macOS not implemented.
/// Windows 7: alpha ignored; Windows 8+: translucent not supported, alpha is 0 or 255.
#[no_mangle]
pub extern "C" fn wry_window_set_background_color(
    app: *mut WryApp,
    window_id: usize,
    r: u8,
    g: u8,
    b: u8,
    a: u8,
) {
    if let Some(win) = get_pending_window(app, window_id) {
        win.pending_background_color = Some((r, g, b, a));
    }
}

/// Set background throttling policy. Must be called before `wry_app_run()`.
/// Values: 0 = Disabled, 1 = Suspend (default browser behavior), 2 = Throttle.
///
/// Platform: Linux / Windows / Android unsupported. macOS 14+, iOS 17+.
#[no_mangle]
pub extern "C" fn wry_window_set_background_throttling(
    app: *mut WryApp,
    window_id: usize,
    policy: c_int,
) {
    if let Some(win) = get_pending_window(app, window_id) {
        win.pending_background_throttling = Some(policy);
    }
}

/// Set the webview theme (Windows only).
/// Values: 0 = Auto (follow OS), 1 = Dark, 2 = Light.
/// Must be called before `wry_app_run()`.
#[no_mangle]
pub extern "C" fn wry_window_set_theme(
    app: *mut WryApp,
    window_id: usize,
    theme: c_int,
) {
    if let Some(win) = get_pending_window(app, window_id) {
        #[cfg(target_os = "windows")]
        {
            win.pending_theme = theme;
        }
        let _ = (win, theme); // suppress unused warnings on non-Windows
    }
}

/// Set whether custom protocols use https:// scheme (Windows only).
/// Default is false (uses http://).
/// Must be called before `wry_app_run()`.
#[no_mangle]
pub extern "C" fn wry_window_set_https_scheme(
    app: *mut WryApp,
    window_id: usize,
    enabled: bool,
) {
    if let Some(win) = get_pending_window(app, window_id) {
        #[cfg(target_os = "windows")]
        {
            win.pending_https_scheme = enabled;
        }
        let _ = (win, enabled);
    }
}

/// Enable/disable browser-specific accelerator keys (Windows only).
/// Default is true. Must be called before `wry_app_run()`.
#[no_mangle]
pub extern "C" fn wry_window_set_browser_accelerator_keys(
    app: *mut WryApp,
    window_id: usize,
    enabled: bool,
) {
    if let Some(win) = get_pending_window(app, window_id) {
        #[cfg(target_os = "windows")]
        {
            win.pending_browser_accelerator_keys = enabled;
        }
        let _ = (win, enabled);
    }
}

/// Enable/disable default context menus in the webview (Windows only).
/// Default is true. Must be called before `wry_app_run()`.
#[no_mangle]
pub extern "C" fn wry_window_set_default_context_menus(
    app: *mut WryApp,
    window_id: usize,
    enabled: bool,
) {
    if let Some(win) = get_pending_window(app, window_id) {
        #[cfg(target_os = "windows")]
        {
            win.pending_default_context_menus = enabled;
        }
        let _ = (win, enabled);
    }
}

/// Set the scrollbar style (Windows only).
/// Values: 0 = Default, 1 = FluentOverlay.
/// Must be called before `wry_app_run()`.
#[no_mangle]
pub extern "C" fn wry_window_set_scroll_bar_style(
    app: *mut WryApp,
    window_id: usize,
    style: c_int,
) {
    if let Some(win) = get_pending_window(app, window_id) {
        #[cfg(target_os = "windows")]
        {
            win.pending_scroll_bar_style = style;
        }
        let _ = (win, style);
    }
}

/// Center the window on the primary monitor. If the window is not yet created,
/// this sets the position to center based on the pending size (applied at
/// creation time using a best-effort calculation).
#[no_mangle]
pub extern "C" fn wry_window_center(app: *mut WryApp, window_id: usize) {
    if let Some(win) = get_pending_window(app, window_id) {
        if let Some(ref w) = win.window {
            // Center on current monitor
            if let Some(monitor) = w.current_monitor() {
                let screen_size = monitor.size();
                let window_size = w.outer_size();
                let x =
                    (screen_size.width as i32 - window_size.width as i32) / 2;
                let y =
                    (screen_size.height as i32 - window_size.height as i32) / 2;
                w.set_outer_position(tao::dpi::PhysicalPosition::new(
                    x.max(0),
                    y.max(0),
                ));
            }
        } else {
            // Will center when created; set a sentinel
            win.pending_position = None; // let tao choose default (centered-ish)
        }
    }
}

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
    } else {
        if !width.is_null() {
            unsafe { *width = win.pending_size.0 as c_int };
        }
        if !height.is_null() {
            unsafe { *height = win.pending_size.1 as c_int };
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
    } else {
        let (px, py) = win.pending_position.unwrap_or((0, 0));
        if !x.is_null() {
            unsafe { *x = px };
        }
        if !y.is_null() {
            unsafe { *y = py };
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
        win.pending_title.clone()
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
        win.pending_resizable
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
        win.pending_fullscreen
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
        win.pending_maximized
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
        win.pending_minimized
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
        win.pending_visible
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
        win.pending_decorations
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
    // Before creation, return the pending URL if set
    if let Some(ref url) = win.pending_url {
        return CString::new(url.as_str())
            .map(|cs| cs.into_raw())
            .unwrap_or(std::ptr::null_mut());
    }
    std::ptr::null_mut()
}

// ---------------------------------------------------------------------------
// Post-run window property setters (via *mut WryWindow from callbacks)
// ---------------------------------------------------------------------------

/// Set the window title. Call from a callback with the WryWindow pointer.
#[no_mangle]
pub extern "C" fn wry_window_set_title_direct(win: *mut WryWindow, title: *const c_char) {
    if win.is_null() {
        return;
    }
    let win = unsafe { &mut *win };
    let title = unsafe { c_str_to_string(title) };
    if let Some(ref w) = win.window {
        w.set_title(&title);
    }
    win.pending_title = title;
}

/// Navigate to a URL. Call from a callback with the WryWindow pointer.
#[no_mangle]
pub extern "C" fn wry_window_load_url_direct(win: *mut WryWindow, url: *const c_char) {
    if win.is_null() {
        return;
    }
    let win = unsafe { &mut *win };
    let url = unsafe { c_str_to_string(url) };
    if let Some(ref wv) = win.webview {
        log_err!(wv.load_url(&url), "load_url_direct");
    }
}

/// Load HTML content. Call from a callback with the WryWindow pointer.
#[no_mangle]
pub extern "C" fn wry_window_load_html_direct(win: *mut WryWindow, html: *const c_char) {
    if win.is_null() {
        return;
    }
    let win = unsafe { &mut *win };
    let html = unsafe { c_str_to_string(html) };
    if let Some(ref wv) = win.webview {
        log_err!(wv.load_html(&html), "load_html_direct");
    }
}

/// Set window size. Call from a callback with the WryWindow pointer.
#[no_mangle]
pub extern "C" fn wry_window_set_size_direct(
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
pub extern "C" fn wry_window_set_position_direct(
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

/// Set window decorations. Call from a callback with the WryWindow pointer.
#[no_mangle]
pub extern "C" fn wry_window_set_decorations_direct(win: *mut WryWindow, decorations: bool) {
    if win.is_null() {
        return;
    }
    let win = unsafe { &mut *win };
    if let Some(ref w) = win.window {
        w.set_decorations(decorations);
    }
    win.pending_decorations = decorations;
}

/// Set webview zoom level. Call from a callback with the WryWindow pointer.
/// 1.0 = 100%, 2.0 = 200%, etc.
#[no_mangle]
pub extern "C" fn wry_window_set_zoom_direct(win: *mut WryWindow, zoom: f64) {
    if win.is_null() {
        return;
    }
    let win = unsafe { &mut *win };
    let z = if zoom > 0.0 { zoom } else { 1.0 };
    if let Some(ref wv) = win.webview {
        log_err!(wv.zoom(z), "zoom_direct");
    }
    win.pending_zoom = z;
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
pub extern "C" fn wry_window_set_fullscreen_direct(win: *mut WryWindow, fullscreen: bool) {
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
pub extern "C" fn wry_window_set_maximized_direct(win: *mut WryWindow, maximized: bool) {
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
pub extern "C" fn wry_window_set_minimized_direct(win: *mut WryWindow, minimized: bool) {
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
pub extern "C" fn wry_window_set_topmost_direct(win: *mut WryWindow, topmost: bool) {
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
pub extern "C" fn wry_window_set_visible_direct(win: *mut WryWindow, visible: bool) {
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
pub extern "C" fn wry_window_set_resizable_direct(win: *mut WryWindow, resizable: bool) {
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
pub extern "C" fn wry_window_center_direct(win: *mut WryWindow) {
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
pub extern "C" fn wry_window_set_background_color_direct(
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
// Event callbacks (pre-run via app+id)
// ---------------------------------------------------------------------------

/// Set the close-requested callback. Return true to allow close, false to
/// prevent. Must be called before `wry_app_run()`.
#[no_mangle]
pub extern "C" fn wry_window_on_close(
    app: *mut WryApp,
    window_id: usize,
    callback: CloseCallback,
    ctx: *mut c_void,
) {
    if let Some(win) = get_pending_window(app, window_id) {
        win.close_handler = Some((callback, ctx as usize));
    }
}

/// Set the window-resized callback. Must be called before `wry_app_run()`.
#[no_mangle]
pub extern "C" fn wry_window_on_resize(
    app: *mut WryApp,
    window_id: usize,
    callback: ResizeCallback,
    ctx: *mut c_void,
) {
    if let Some(win) = get_pending_window(app, window_id) {
        win.resize_handler = Some((callback, ctx as usize));
    }
}

/// Set the window-moved callback. Must be called before `wry_app_run()`.
#[no_mangle]
pub extern "C" fn wry_window_on_move(
    app: *mut WryApp,
    window_id: usize,
    callback: MoveCallback,
    ctx: *mut c_void,
) {
    if let Some(win) = get_pending_window(app, window_id) {
        win.move_handler = Some((callback, ctx as usize));
    }
}

/// Set the window focus-change callback. Must be called before `wry_app_run()`.
#[no_mangle]
pub extern "C" fn wry_window_on_focus(
    app: *mut WryApp,
    window_id: usize,
    callback: FocusCallback,
    ctx: *mut c_void,
) {
    if let Some(win) = get_pending_window(app, window_id) {
        win.focus_handler = Some((callback, ctx as usize));
    }
}

/// Set a navigation handler. Called before each navigation with the target URL.
/// Return true to allow the navigation, false to block it.
/// Must be called before `wry_app_run()`.
#[no_mangle]
pub extern "C" fn wry_window_set_navigation_handler(
    app: *mut WryApp,
    window_id: usize,
    callback: NavigationCallback,
    ctx: *mut c_void,
) {
    if let Some(win) = get_pending_window(app, window_id) {
        win.navigation_handler = Some((callback, ctx as usize));
    }
}

/// Set a page load event handler. Called when a page starts loading (event=0)
/// and when it finishes loading (event=1).
/// Must be called before `wry_app_run()`.
#[no_mangle]
pub extern "C" fn wry_window_set_page_load_handler(
    app: *mut WryApp,
    window_id: usize,
    callback: PageLoadCallback,
    ctx: *mut c_void,
) {
    if let Some(win) = get_pending_window(app, window_id) {
        win.page_load_handler = Some((callback, ctx as usize));
    }
}

/// Set a drag-drop event handler. Called when files are dragged/dropped on the
/// webview. The callback receives an event type (0=Enter, 1=Over, 2=Drop,
/// 3=Leave), an array of file path strings, the path count, and the cursor
/// position (x, y) relative to the webview.
///
/// Return true to block the OS default behavior (which enables native file
/// drop on `<input type="file">`).
///
/// Must be called before `wry_app_run()`.
#[no_mangle]
pub extern "C" fn wry_window_on_drag_drop(
    app: *mut WryApp,
    window_id: usize,
    callback: DragDropCallback,
    ctx: *mut c_void,
) {
    if let Some(win) = get_pending_window(app, window_id) {
        win.drag_drop_handler = Some((callback, ctx as usize));
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
/// returned by `wry_window_new`.
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

