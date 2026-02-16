//! Tray icon types, structs, and C API functions.

#![allow(clippy::missing_safety_doc)]

use std::ffi::{c_char, c_int, c_void, CString};

use tray_icon::TrayIconBuilder;
use tray_icon::menu as tray_menu;

use crate::{WryApp, UserEvent, c_str_to_string};

// ---------------------------------------------------------------------------
// Callback type aliases
// ---------------------------------------------------------------------------

/// Tray icon event callback:
///   fn(event_type: c_int, x: f64, y: f64,
///      icon_x: f64, icon_y: f64, icon_w: u32, icon_h: u32,
///      button: c_int, button_state: c_int, ctx: *mut c_void)
///
/// - `event_type`: 0=Click, 1=DoubleClick, 2=Enter, 3=Move, 4=Leave
/// - `x`, `y`: mouse position (physical pixels)
/// - `icon_x`, `icon_y`, `icon_w`, `icon_h`: tray icon rect
/// - `button`: 0=Left, 1=Right, 2=Middle (only for Click/DoubleClick)
/// - `button_state`: 0=Up, 1=Down (only for Click)
type TrayEventCallback =
    extern "C" fn(c_int, f64, f64, f64, f64, u32, u32, c_int, c_int, *mut c_void);

/// Tray context menu item clicked callback: fn(item_id: *const c_char, ctx: *mut c_void)
type TrayMenuEventCallback = extern "C" fn(*const c_char, *mut c_void);

/// Tray dispatch callback: fn(tray: *mut WryTray, ctx: *mut c_void)
pub(crate) type TrayDispatchCallback = extern "C" fn(*mut WryTray, *mut c_void);

// ---------------------------------------------------------------------------
// Tray menu building helpers
// ---------------------------------------------------------------------------

pub struct WryTrayMenu {
    items: Vec<WryTrayMenuItem>,
}

enum WryTrayMenuItem {
    Item { id: String, label: String, enabled: bool },
    Check { id: String, label: String, checked: bool, enabled: bool },
    Separator,
    Submenu { label: String, enabled: bool, menu: WryTrayMenu },
}

impl WryTrayMenuItem {
    fn append_to_menu(&self, menu: &tray_menu::Menu) {
        match self {
            WryTrayMenuItem::Item { id, label, enabled } => {
                let mi = tray_menu::MenuItem::with_id(id.as_str(), label, *enabled, None);
                let _ = menu.append(&mi);
            }
            WryTrayMenuItem::Check { id, label, checked, enabled } => {
                let mi = tray_menu::CheckMenuItem::with_id(
                    id.as_str(), label, *enabled, *checked, None,
                );
                let _ = menu.append(&mi);
            }
            WryTrayMenuItem::Separator => {
                let _ = menu.append(&tray_menu::PredefinedMenuItem::separator());
            }
            WryTrayMenuItem::Submenu { label, enabled, menu: sub } => {
                let submenu = tray_menu::Submenu::new(label, *enabled);
                sub.append_items_to_submenu(&submenu);
                let _ = menu.append(&submenu);
            }
        }
    }

    fn append_to_submenu(&self, target: &tray_menu::Submenu) {
        match self {
            WryTrayMenuItem::Item { id, label, enabled } => {
                let mi = tray_menu::MenuItem::with_id(id.as_str(), label, *enabled, None);
                let _ = target.append(&mi);
            }
            WryTrayMenuItem::Check { id, label, checked, enabled } => {
                let mi = tray_menu::CheckMenuItem::with_id(
                    id.as_str(), label, *enabled, *checked, None,
                );
                let _ = target.append(&mi);
            }
            WryTrayMenuItem::Separator => {
                let _ = target.append(&tray_menu::PredefinedMenuItem::separator());
            }
            WryTrayMenuItem::Submenu { label, enabled, menu: sub } => {
                let submenu = tray_menu::Submenu::new(label, *enabled);
                sub.append_items_to_submenu(&submenu);
                let _ = target.append(&submenu);
            }
        }
    }
}

impl WryTrayMenu {
    fn append_items_to_submenu(&self, submenu: &tray_menu::Submenu) {
        for item in &self.items {
            item.append_to_submenu(submenu);
        }
    }

    fn build(&self) -> tray_menu::Menu {
        let menu = tray_menu::Menu::new();
        for item in &self.items {
            item.append_to_menu(&menu);
        }
        menu
    }

    fn collect_ids(&self, ids: &mut Vec<String>) {
        for item in &self.items {
            match item {
                WryTrayMenuItem::Item { id, .. } | WryTrayMenuItem::Check { id, .. } => {
                    ids.push(id.clone());
                }
                WryTrayMenuItem::Submenu { menu, .. } => {
                    menu.collect_ids(ids);
                }
                _ => {}
            }
        }
    }
}

// ---------------------------------------------------------------------------
// WryTray -- per-tray-icon state
// ---------------------------------------------------------------------------

pub struct WryTray {
    pub(crate) id: usize,

    // --- Pending config (set before app_run) ---
    pending_tooltip: Option<String>,
    pending_title: Option<String>,
    pending_icon_rgba: Option<(Vec<u8>, u32, u32)>,
    pending_menu: Option<Box<WryTrayMenu>>,
    pending_menu_on_left_click: bool,
    pending_visible: bool,
    pending_icon_is_template: bool,

    // --- Callbacks ---
    event_handler: Option<(TrayEventCallback, usize)>,
    menu_event_handler: Option<(TrayMenuEventCallback, usize)>,

    // --- Live state (populated during app_run) ---
    tray: Option<tray_icon::TrayIcon>,
    pub(crate) menu_item_ids: Vec<String>,
}

impl WryTray {
    pub(crate) fn new(id: usize) -> Self {
        Self {
            id,
            pending_tooltip: None,
            pending_title: None,
            pending_icon_rgba: None,
            pending_menu: None,
            pending_menu_on_left_click: true,
            pending_visible: true,
            pending_icon_is_template: false,
            event_handler: None,
            menu_event_handler: None,
            tray: None,
            menu_item_ids: Vec::new(),
        }
    }

    pub(crate) fn create(&mut self) {
        let tray_id = tray_icon::TrayIconId::new(self.id.to_string());
        let mut builder = TrayIconBuilder::new().with_id(tray_id);

        if let Some(ref tooltip) = self.pending_tooltip {
            builder = builder.with_tooltip(tooltip);
        }
        if let Some(ref title) = self.pending_title {
            builder = builder.with_title(title);
        }
        if let Some((ref rgba, w, h)) = self.pending_icon_rgba {
            match tray_icon::Icon::from_rgba(rgba.clone(), w, h) {
                Ok(icon) => { builder = builder.with_icon(icon); }
                Err(e) => { eprintln!("[wry-native] tray icon from_rgba failed: {}", e); }
            }
        }
        if let Some(ref menu_data) = self.pending_menu {
            let muda_menu = menu_data.build();
            menu_data.collect_ids(&mut self.menu_item_ids);
            builder = builder.with_menu(Box::new(muda_menu));
        }
        builder = builder.with_menu_on_left_click(self.pending_menu_on_left_click);
        builder = builder.with_icon_as_template(self.pending_icon_is_template);

        match builder.build() {
            Ok(tray) => {
                if !self.pending_visible {
                    log_err!(tray.set_visible(false), "tray set_visible(false)");
                }
                self.tray = Some(tray);
            }
            Err(e) => {
                eprintln!("[wry-native] tray icon build failed: {}", e);
            }
        }
    }

    /// Dispatch a tray icon event (click, double-click, etc.) to the C callback.
    pub(crate) fn handle_tray_event(&self, event: &tray_icon::TrayIconEvent) {
        let Some((cb, ctx)) = self.event_handler else { return; };
        use tray_icon::TrayIconEvent as TIE;
        let (evt, x, y, ix, iy, iw, ih, btn, st) = match event {
            TIE::Click { position, rect, button, button_state, .. } => {
                let b = match button {
                    tray_icon::MouseButton::Left => 0,
                    tray_icon::MouseButton::Right => 1,
                    tray_icon::MouseButton::Middle => 2,
                };
                let s = match button_state {
                    tray_icon::MouseButtonState::Up => 0,
                    tray_icon::MouseButtonState::Down => 1,
                };
                (0, position.x, position.y, rect.position.x, rect.position.y,
                 rect.size.width, rect.size.height, b, s)
            }
            TIE::DoubleClick { position, rect, button, .. } => {
                let b = match button {
                    tray_icon::MouseButton::Left => 0,
                    tray_icon::MouseButton::Right => 1,
                    tray_icon::MouseButton::Middle => 2,
                };
                (1, position.x, position.y, rect.position.x, rect.position.y,
                 rect.size.width, rect.size.height, b, 0)
            }
            TIE::Enter { position, rect, .. } => {
                (2, position.x, position.y, rect.position.x, rect.position.y,
                 rect.size.width, rect.size.height, 0, 0)
            }
            TIE::Move { position, rect, .. } => {
                (3, position.x, position.y, rect.position.x, rect.position.y,
                 rect.size.width, rect.size.height, 0, 0)
            }
            TIE::Leave { position, rect, .. } => {
                (4, position.x, position.y, rect.position.x, rect.position.y,
                 rect.size.width, rect.size.height, 0, 0)
            }
            _ => { return; }
        };
        cb(evt as c_int, x, y, ix, iy, iw, ih,
           btn as c_int, st as c_int, ctx as *mut c_void);
    }

    /// Dispatch a tray menu item event to the C callback.
    pub(crate) fn handle_menu_event(&self, menu_id: &str) {
        let Some((cb, ctx)) = self.menu_event_handler else { return; };
        if let Ok(c_id) = CString::new(menu_id) {
            cb(c_id.as_ptr(), ctx as *mut c_void);
        }
    }

    /// Execute a dispatched C callback with a pointer to this tray.
    pub(crate) fn handle_dispatch(&mut self, callback: TrayDispatchCallback, ctx: usize) {
        let tray_ptr = self as *mut WryTray;
        callback(tray_ptr, ctx as *mut c_void);
    }
}

// ---------------------------------------------------------------------------
// Event handler setup (called from lib.rs before event loop)
// ---------------------------------------------------------------------------

/// Wire up the global tray icon and menu event handlers to forward events
/// into the tao event loop via the proxy.
pub(crate) fn setup_tray_event_handlers(
    proxy: &tao::event_loop::EventLoopProxy<UserEvent>,
) {
    let proxy_tray = proxy.clone();
    tray_icon::TrayIconEvent::set_event_handler(Some(move |event| {
        let _ = proxy_tray.send_event(UserEvent::TrayEvent(event));
    }));
    let proxy_menu = proxy.clone();
    tray_menu::MenuEvent::set_event_handler(Some(move |event| {
        let _ = proxy_menu.send_event(UserEvent::TrayMenuEvent(event));
    }));
}

// ---------------------------------------------------------------------------
// Helper: look up a pending WryTray by ID (pre-run only).
// ---------------------------------------------------------------------------

fn get_pending_tray(app: *mut WryApp, tray_id: usize) -> Option<&'static mut WryTray> {
    if app.is_null() {
        return None;
    }
    let app = unsafe { &mut *app };
    app.trays.get_mut(&tray_id).map(|t| {
        unsafe { &mut *(t as *mut WryTray) }
    })
}

// ===========================================================================
// EXPORTED C API
// ===========================================================================

// ---------------------------------------------------------------------------
// Tray menu building
// ---------------------------------------------------------------------------

/// Create a new tray menu. Returns an opaque handle.
/// Free with `wry_tray_menu_destroy` if not consumed by `wry_tray_set_menu`.
#[no_mangle]
pub extern "C" fn wry_tray_menu_new() -> *mut WryTrayMenu {
    Box::into_raw(Box::new(WryTrayMenu { items: Vec::new() }))
}

/// Add a clickable menu item.
///
/// - `menu`: menu handle from `wry_tray_menu_new` or `wry_tray_menu_add_submenu`
/// - `id`: unique string ID (returned in the menu event callback)
/// - `label`: display text
/// - `enabled`: whether the item is clickable
#[no_mangle]
pub extern "C" fn wry_tray_menu_add_item(
    menu: *mut WryTrayMenu,
    id: *const c_char,
    label: *const c_char,
    enabled: bool,
) {
    if menu.is_null() { return; }
    let menu = unsafe { &mut *menu };
    let id = unsafe { c_str_to_string(id) };
    let label = unsafe { c_str_to_string(label) };
    menu.items.push(WryTrayMenuItem::Item { id, label, enabled });
}

/// Add a checkable menu item.
///
/// - `id`: unique string ID
/// - `label`: display text
/// - `checked`: initial checked state
/// - `enabled`: whether the item is clickable
#[no_mangle]
pub extern "C" fn wry_tray_menu_add_check_item(
    menu: *mut WryTrayMenu,
    id: *const c_char,
    label: *const c_char,
    checked: bool,
    enabled: bool,
) {
    if menu.is_null() { return; }
    let menu = unsafe { &mut *menu };
    let id = unsafe { c_str_to_string(id) };
    let label = unsafe { c_str_to_string(label) };
    menu.items.push(WryTrayMenuItem::Check { id, label, checked, enabled });
}

/// Add a separator line.
#[no_mangle]
pub extern "C" fn wry_tray_menu_add_separator(menu: *mut WryTrayMenu) {
    if menu.is_null() { return; }
    let menu = unsafe { &mut *menu };
    menu.items.push(WryTrayMenuItem::Separator);
}

/// Add a submenu. Returns a handle to the submenu (valid as long as the
/// parent menu is alive). Add items to it with the normal menu functions.
#[no_mangle]
pub extern "C" fn wry_tray_menu_add_submenu(
    menu: *mut WryTrayMenu,
    label: *const c_char,
    enabled: bool,
) -> *mut WryTrayMenu {
    if menu.is_null() { return std::ptr::null_mut(); }
    let menu = unsafe { &mut *menu };
    let label = unsafe { c_str_to_string(label) };
    menu.items.push(WryTrayMenuItem::Submenu {
        label,
        enabled,
        menu: WryTrayMenu { items: Vec::new() },
    });
    if let Some(WryTrayMenuItem::Submenu { menu: ref mut sub, .. }) = menu.items.last_mut() {
        sub as *mut WryTrayMenu
    } else {
        std::ptr::null_mut()
    }
}

/// Free a tray menu that was NOT consumed by `wry_tray_set_menu`.
/// Do NOT call this on menus that were already passed to `wry_tray_set_menu`
/// or on submenu pointers returned by `wry_tray_menu_add_submenu`.
#[no_mangle]
pub extern "C" fn wry_tray_menu_destroy(menu: *mut WryTrayMenu) {
    if !menu.is_null() {
        unsafe { drop(Box::from_raw(menu)); }
    }
}

// ---------------------------------------------------------------------------
// Tray lifecycle (pre-run configuration)
// ---------------------------------------------------------------------------

/// Create a new tray icon handle. Returns an opaque tray ID used in
/// subsequent calls. Returns 0 on failure. The tray is materialized
/// when `wry_app_run()` is called.
#[no_mangle]
pub extern "C" fn wry_tray_new(app: *mut WryApp) -> usize {
    if app.is_null() { return 0; }
    let app = unsafe { &mut *app };
    let id = app.next_tray_id;
    app.next_tray_id += 1;
    let tray = WryTray::new(id);
    app.trays.insert(id, tray);
    id
}

/// Set the tray icon from raw RGBA pixel data. Must be called before `wry_app_run()`.
///
/// - `rgba`: pointer to RGBA pixel data (4 bytes per pixel, row-major)
/// - `rgba_len`: total byte length (must equal width * height * 4)
/// - `width`, `height`: icon dimensions in pixels
#[no_mangle]
pub extern "C" fn wry_tray_set_icon(
    app: *mut WryApp,
    tray_id: usize,
    rgba: *const u8,
    rgba_len: c_int,
    width: c_int,
    height: c_int,
) {
    if let Some(tray) = get_pending_tray(app, tray_id) {
        if rgba.is_null() || rgba_len <= 0 || width <= 0 || height <= 0 {
            tray.pending_icon_rgba = None;
            return;
        }
        let data = unsafe { std::slice::from_raw_parts(rgba, rgba_len as usize) }.to_vec();
        tray.pending_icon_rgba = Some((data, width as u32, height as u32));
    }
}

/// Set the tray icon from encoded image file bytes (PNG, ICO, JPEG, BMP, GIF).
/// Must be called before `wry_app_run()`.
#[no_mangle]
pub extern "C" fn wry_tray_set_icon_from_bytes(
    app: *mut WryApp,
    tray_id: usize,
    data: *const u8,
    data_len: c_int,
) {
    if let Some(tray) = get_pending_tray(app, tray_id) {
        if data.is_null() || data_len <= 0 {
            tray.pending_icon_rgba = None;
            return;
        }
        let bytes = unsafe { std::slice::from_raw_parts(data, data_len as usize) };
        match image::load_from_memory(bytes) {
            Ok(img) => {
                use image::GenericImageView;
                let rgba = img.to_rgba8();
                let (w, h) = img.dimensions();
                tray.pending_icon_rgba = Some((rgba.into_raw(), w, h));
            }
            Err(e) => {
                eprintln!("[wry-native] tray icon image decode failed: {}", e);
            }
        }
    }
}

/// Set the tray tooltip. Must be called before `wry_app_run()`.
///
/// Platform: Linux - unsupported.
#[no_mangle]
pub extern "C" fn wry_tray_set_tooltip(
    app: *mut WryApp,
    tray_id: usize,
    tooltip: *const c_char,
) {
    if let Some(tray) = get_pending_tray(app, tray_id) {
        let s = unsafe { c_str_to_string(tooltip) };
        tray.pending_tooltip = if s.is_empty() { None } else { Some(s) };
    }
}

/// Set the tray title. Must be called before `wry_app_run()`.
///
/// Platform: macOS and Linux only. Windows - unsupported.
#[no_mangle]
pub extern "C" fn wry_tray_set_title(
    app: *mut WryApp,
    tray_id: usize,
    title: *const c_char,
) {
    if let Some(tray) = get_pending_tray(app, tray_id) {
        let s = unsafe { c_str_to_string(title) };
        tray.pending_title = if s.is_empty() { None } else { Some(s) };
    }
}

/// Assign a context menu to the tray icon. Takes ownership of the menu -
/// do NOT call `wry_tray_menu_destroy` on it after this.
/// Must be called before `wry_app_run()`.
#[no_mangle]
pub extern "C" fn wry_tray_set_menu(
    app: *mut WryApp,
    tray_id: usize,
    menu: *mut WryTrayMenu,
) {
    if let Some(tray) = get_pending_tray(app, tray_id) {
        if menu.is_null() {
            tray.pending_menu = None;
        } else {
            tray.pending_menu = Some(unsafe { Box::from_raw(menu) });
        }
    }
}

/// Whether to show the tray menu on left click (default: true).
/// Must be called before `wry_app_run()`.
///
/// Platform: Linux - unsupported.
#[no_mangle]
pub extern "C" fn wry_tray_set_menu_on_left_click(
    app: *mut WryApp,
    tray_id: usize,
    enable: bool,
) {
    if let Some(tray) = get_pending_tray(app, tray_id) {
        tray.pending_menu_on_left_click = enable;
    }
}

/// Set initial tray visibility (default: true).
/// Must be called before `wry_app_run()`.
#[no_mangle]
pub extern "C" fn wry_tray_set_visible(
    app: *mut WryApp,
    tray_id: usize,
    visible: bool,
) {
    if let Some(tray) = get_pending_tray(app, tray_id) {
        tray.pending_visible = visible;
    }
}

/// Use the icon as a template icon. macOS only.
/// Must be called before `wry_app_run()`.
#[no_mangle]
pub extern "C" fn wry_tray_set_icon_as_template(
    app: *mut WryApp,
    tray_id: usize,
    is_template: bool,
) {
    if let Some(tray) = get_pending_tray(app, tray_id) {
        tray.pending_icon_is_template = is_template;
    }
}

// ---------------------------------------------------------------------------
// Tray callbacks (pre-run)
// ---------------------------------------------------------------------------

/// Register a callback for tray icon events (click, double-click, enter, move, leave).
/// Must be called before `wry_app_run()`.
///
/// Platform: Linux - events are not emitted.
#[no_mangle]
pub extern "C" fn wry_tray_on_event(
    app: *mut WryApp,
    tray_id: usize,
    callback: TrayEventCallback,
    ctx: *mut c_void,
) {
    if let Some(tray) = get_pending_tray(app, tray_id) {
        tray.event_handler = Some((callback, ctx as usize));
    }
}

/// Register a callback for tray context menu item clicks.
/// The callback receives the item's string ID.
/// Must be called before `wry_app_run()`.
#[no_mangle]
pub extern "C" fn wry_tray_on_menu_event(
    app: *mut WryApp,
    tray_id: usize,
    callback: TrayMenuEventCallback,
    ctx: *mut c_void,
) {
    if let Some(tray) = get_pending_tray(app, tray_id) {
        tray.menu_event_handler = Some((callback, ctx as usize));
    }
}

// ---------------------------------------------------------------------------
// Tray post-run (direct) -- call from dispatch callback or event handler
// ---------------------------------------------------------------------------

/// Set the tray icon from raw RGBA pixel data at runtime.
#[no_mangle]
pub extern "C" fn wry_tray_set_icon_direct(
    tray: *mut WryTray,
    rgba: *const u8,
    rgba_len: c_int,
    width: c_int,
    height: c_int,
) {
    if tray.is_null() { return; }
    let tray = unsafe { &mut *tray };
    if let Some(ref t) = tray.tray {
        if rgba.is_null() || rgba_len <= 0 || width <= 0 || height <= 0 {
            log_err!(t.set_icon(None), "tray set_icon(None)");
            return;
        }
        let data = unsafe { std::slice::from_raw_parts(rgba, rgba_len as usize) }.to_vec();
        match tray_icon::Icon::from_rgba(data, width as u32, height as u32) {
            Ok(icon) => { log_err!(t.set_icon(Some(icon)), "tray set_icon"); }
            Err(e) => { eprintln!("[wry-native] tray set_icon_direct from_rgba failed: {}", e); }
        }
    }
}

/// Set the tray icon from encoded image file bytes at runtime.
#[no_mangle]
pub extern "C" fn wry_tray_set_icon_from_bytes_direct(
    tray: *mut WryTray,
    data: *const u8,
    data_len: c_int,
) {
    if tray.is_null() { return; }
    let tray = unsafe { &mut *tray };
    if let Some(ref t) = tray.tray {
        if data.is_null() || data_len <= 0 {
            log_err!(t.set_icon(None), "tray set_icon(None)");
            return;
        }
        let bytes = unsafe { std::slice::from_raw_parts(data, data_len as usize) };
        match image::load_from_memory(bytes) {
            Ok(img) => {
                use image::GenericImageView;
                let rgba = img.to_rgba8();
                let (w, h) = img.dimensions();
                match tray_icon::Icon::from_rgba(rgba.into_raw(), w, h) {
                    Ok(icon) => { log_err!(t.set_icon(Some(icon)), "tray set_icon"); }
                    Err(e) => { eprintln!("[wry-native] tray icon from_rgba failed: {}", e); }
                }
            }
            Err(e) => {
                eprintln!("[wry-native] tray icon image decode failed: {}", e);
            }
        }
    }
}

/// Set the tray tooltip at runtime.
///
/// Platform: Linux - unsupported.
#[no_mangle]
pub extern "C" fn wry_tray_set_tooltip_direct(tray: *mut WryTray, tooltip: *const c_char) {
    if tray.is_null() { return; }
    let tray = unsafe { &mut *tray };
    if let Some(ref t) = tray.tray {
        let s = unsafe { c_str_to_string(tooltip) };
        let val: Option<&str> = if s.is_empty() { None } else { Some(&s) };
        log_err!(t.set_tooltip(val), "tray set_tooltip");
    }
}

/// Set the tray title at runtime.
///
/// Platform: macOS and Linux only. Windows - unsupported.
#[no_mangle]
pub extern "C" fn wry_tray_set_title_direct(tray: *mut WryTray, title: *const c_char) {
    if tray.is_null() { return; }
    let tray = unsafe { &mut *tray };
    if let Some(ref t) = tray.tray {
        let s = unsafe { c_str_to_string(title) };
        let val: Option<&str> = if s.is_empty() { None } else { Some(&s) };
        t.set_title(val);
    }
}

/// Show or hide the tray icon at runtime.
#[no_mangle]
pub extern "C" fn wry_tray_set_visible_direct(tray: *mut WryTray, visible: bool) {
    if tray.is_null() { return; }
    let tray = unsafe { &mut *tray };
    if let Some(ref t) = tray.tray {
        log_err!(t.set_visible(visible), "tray set_visible");
    }
}

/// Replace the tray context menu at runtime. Takes ownership of the menu.
#[no_mangle]
pub extern "C" fn wry_tray_set_menu_direct(tray: *mut WryTray, menu: *mut WryTrayMenu) {
    if tray.is_null() { return; }
    let tray = unsafe { &mut *tray };
    if let Some(ref t) = tray.tray {
        if menu.is_null() {
            t.set_menu(None);
        } else {
            let menu_data = unsafe { Box::from_raw(menu) };
            let muda_menu = menu_data.build();
            t.set_menu(Some(Box::new(muda_menu)));
        }
    }
}

/// Enable or disable showing the tray menu on left click at runtime.
///
/// Platform: Linux - unsupported.
#[no_mangle]
pub extern "C" fn wry_tray_set_menu_on_left_click_direct(tray: *mut WryTray, enable: bool) {
    if tray.is_null() { return; }
    let tray = unsafe { &mut *tray };
    if let Some(ref t) = tray.tray {
        t.set_show_menu_on_left_click(enable);
    }
}

/// Use the icon as a template icon at runtime. macOS only.
#[no_mangle]
pub extern "C" fn wry_tray_set_icon_as_template_direct(tray: *mut WryTray, is_template: bool) {
    if tray.is_null() { return; }
    let tray = unsafe { &mut *tray };
    if let Some(ref t) = tray.tray {
        t.set_icon_as_template(is_template);
    }
}

// ---------------------------------------------------------------------------
// Tray cross-thread dispatch
// ---------------------------------------------------------------------------

/// Dispatch a callback to run on the event loop (main) thread for a tray.
/// Safe to call from any thread.
#[no_mangle]
pub extern "C" fn wry_tray_dispatch(
    app: *mut WryApp,
    tray_id: usize,
    callback: TrayDispatchCallback,
    ctx: *mut c_void,
) {
    if app.is_null() { return; }
    let app = unsafe { &*app };
    log_err!(app.proxy.send_event(UserEvent::TrayDispatch {
        tray_id,
        callback,
        ctx: ctx as usize,
    }), "tray dispatch");
}

/// Remove a tray icon. Safe to call from any thread.
/// After removal, the event loop will exit if no windows or trays remain.
#[no_mangle]
pub extern "C" fn wry_tray_remove(app: *mut WryApp, tray_id: usize) {
    if app.is_null() { return; }
    let app = unsafe { &*app };
    log_err!(app.proxy.send_event(UserEvent::TrayRemove {
        tray_id,
    }), "tray remove");
}
