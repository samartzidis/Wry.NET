//! Tray icon types, structs, and C API functions.

#![allow(clippy::missing_safety_doc)]

use std::collections::HashMap;
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
    Submenu { id: String, label: String, enabled: bool, menu: WryTrayMenu },
}

/// A live muda menu item handle, keyed by user-provided string ID.
pub(crate) enum LiveMenuItem {
    Item(tray_menu::MenuItem),
    Check(tray_menu::CheckMenuItem),
    Submenu(tray_menu::Submenu),
}

impl LiveMenuItem {
    fn text(&self) -> String {
        match self {
            Self::Item(i) => i.text(),
            Self::Check(i) => i.text(),
            Self::Submenu(i) => i.text(),
        }
    }

    fn set_text(&self, text: &str) {
        match self {
            Self::Item(i) => i.set_text(text),
            Self::Check(i) => i.set_text(text),
            Self::Submenu(i) => i.set_text(text),
        }
    }

    fn is_enabled(&self) -> bool {
        match self {
            Self::Item(i) => i.is_enabled(),
            Self::Check(i) => i.is_enabled(),
            Self::Submenu(i) => i.is_enabled(),
        }
    }

    fn set_enabled(&self, enabled: bool) {
        match self {
            Self::Item(i) => i.set_enabled(enabled),
            Self::Check(i) => i.set_enabled(enabled),
            Self::Submenu(i) => i.set_enabled(enabled),
        }
    }

    fn as_is_menu_item(&self) -> &dyn tray_menu::IsMenuItem {
        match self {
            Self::Item(i) => i,
            Self::Check(i) => i,
            Self::Submenu(i) => i,
        }
    }
}

impl WryTrayMenuItem {
    fn append_to_menu(
        &self,
        menu: &tray_menu::Menu,
        live: &mut HashMap<String, LiveMenuItem>,
    ) {
        match self {
            WryTrayMenuItem::Item { id, label, enabled } => {
                let mi = tray_menu::MenuItem::with_id(id.as_str(), label, *enabled, None);
                let _ = menu.append(&mi);
                live.insert(id.clone(), LiveMenuItem::Item(mi));
            }
            WryTrayMenuItem::Check { id, label, checked, enabled } => {
                let mi = tray_menu::CheckMenuItem::with_id(
                    id.as_str(), label, *enabled, *checked, None,
                );
                let _ = menu.append(&mi);
                live.insert(id.clone(), LiveMenuItem::Check(mi));
            }
            WryTrayMenuItem::Separator => {
                let _ = menu.append(&tray_menu::PredefinedMenuItem::separator());
            }
            WryTrayMenuItem::Submenu { id, label, enabled, menu: sub } => {
                let submenu = tray_menu::Submenu::with_id(id.as_str(), label, *enabled);
                sub.append_items_to_submenu(&submenu, live);
                let _ = menu.append(&submenu);
                live.insert(id.clone(), LiveMenuItem::Submenu(submenu));
            }
        }
    }

    fn append_to_submenu(
        &self,
        target: &tray_menu::Submenu,
        live: &mut HashMap<String, LiveMenuItem>,
    ) {
        match self {
            WryTrayMenuItem::Item { id, label, enabled } => {
                let mi = tray_menu::MenuItem::with_id(id.as_str(), label, *enabled, None);
                let _ = target.append(&mi);
                live.insert(id.clone(), LiveMenuItem::Item(mi));
            }
            WryTrayMenuItem::Check { id, label, checked, enabled } => {
                let mi = tray_menu::CheckMenuItem::with_id(
                    id.as_str(), label, *enabled, *checked, None,
                );
                let _ = target.append(&mi);
                live.insert(id.clone(), LiveMenuItem::Check(mi));
            }
            WryTrayMenuItem::Separator => {
                let _ = target.append(&tray_menu::PredefinedMenuItem::separator());
            }
            WryTrayMenuItem::Submenu { id, label, enabled, menu: sub } => {
                let submenu = tray_menu::Submenu::with_id(id.as_str(), label, *enabled);
                sub.append_items_to_submenu(&submenu, live);
                let _ = target.append(&submenu);
                live.insert(id.clone(), LiveMenuItem::Submenu(submenu));
            }
        }
    }
}

impl WryTrayMenu {
    fn append_items_to_submenu(
        &self,
        submenu: &tray_menu::Submenu,
        live: &mut HashMap<String, LiveMenuItem>,
    ) {
        for item in &self.items {
            item.append_to_submenu(submenu, live);
        }
    }

    fn build(&self) -> (tray_menu::Menu, HashMap<String, LiveMenuItem>) {
        let menu = tray_menu::Menu::new();
        let mut live = HashMap::new();
        for item in &self.items {
            item.append_to_menu(&menu, &mut live);
        }
        (menu, live)
    }

    fn collect_ids(&self, ids: &mut Vec<String>) {
        for item in &self.items {
            match item {
                WryTrayMenuItem::Item { id, .. }
                | WryTrayMenuItem::Check { id, .. }
                | WryTrayMenuItem::Submenu { id, .. } => {
                    ids.push(id.clone());
                }
                _ => {}
            }
            if let WryTrayMenuItem::Submenu { menu, .. } = item {
                menu.collect_ids(ids);
            }
        }
    }
}

// ---------------------------------------------------------------------------
// WryTrayCreateOptions -- #[repr(C)] struct for create-with-options pattern
// ---------------------------------------------------------------------------

#[repr(C)]
pub struct WryTrayCreateOptions {
    pub tooltip: *const c_char,
    pub title: *const c_char,
    pub icon_data: *const u8,
    pub icon_data_len: c_int,
    pub menu: *mut WryTrayMenu,
    pub menu_on_left_click: c_int, // 1 = true (default)
    pub visible: c_int,            // 1 = true (default)
    pub icon_is_template: c_int,   // 0 = false (default)
    pub event_callback: *const c_void,
    pub event_ctx: *mut c_void,
    pub menu_event_callback: *const c_void,
    pub menu_event_ctx: *mut c_void,
}

/// Parsed payload stored until the event loop materializes the tray icon.
pub(crate) struct TrayCreatePayload {
    tooltip: Option<String>,
    title: Option<String>,
    icon_rgba: Option<(Vec<u8>, u32, u32)>,
    menu: Option<Box<WryTrayMenu>>,
    menu_on_left_click: bool,
    visible: bool,
    icon_is_template: bool,
}

impl TrayCreatePayload {
    fn from_options(opts: &WryTrayCreateOptions) -> Self {
        let tooltip = {
            let s = unsafe { c_str_to_string(opts.tooltip) };
            if s.is_empty() { None } else { Some(s) }
        };
        let title = {
            let s = unsafe { c_str_to_string(opts.title) };
            if s.is_empty() { None } else { Some(s) }
        };

        let icon_rgba = if !opts.icon_data.is_null() && opts.icon_data_len > 0 {
            let bytes = unsafe { std::slice::from_raw_parts(opts.icon_data, opts.icon_data_len as usize) };
            match image::load_from_memory(bytes) {
                Ok(img) => {
                    use image::GenericImageView;
                    let rgba = img.to_rgba8();
                    let (w, h) = img.dimensions();
                    Some((rgba.into_raw(), w, h))
                }
                Err(e) => {
                    eprintln!("[wry-native] tray icon image decode failed: {}", e);
                    None
                }
            }
        } else {
            None
        };

        let menu = if opts.menu.is_null() {
            None
        } else {
            Some(unsafe { Box::from_raw(opts.menu) })
        };

        Self {
            tooltip,
            title,
            icon_rgba,
            menu,
            menu_on_left_click: opts.menu_on_left_click != 0,
            visible: opts.visible != 0,
            icon_is_template: opts.icon_is_template != 0,
        }
    }
}

// ---------------------------------------------------------------------------
// WryTray -- per-tray-icon state
// ---------------------------------------------------------------------------

pub struct WryTray {
    pub(crate) id: usize,

    // --- Callbacks ---
    event_handler: Option<(TrayEventCallback, usize)>,
    menu_event_handler: Option<(TrayMenuEventCallback, usize)>,

    // --- Live state (populated during app_run) ---
    tray: Option<tray_icon::TrayIcon>,
    pub(crate) menu_item_ids: Vec<String>,
    pub(crate) live_items: HashMap<String, LiveMenuItem>,
    live_menu: Option<tray_menu::Menu>,
}

impl WryTray {
    pub(crate) fn new(id: usize) -> Self {
        Self {
            id,
            event_handler: None,
            menu_event_handler: None,
            tray: None,
            menu_item_ids: Vec::new(),
            live_items: HashMap::new(),
            live_menu: None,
        }
    }

    pub(crate) fn create(&mut self, payload: &TrayCreatePayload) {
        let tray_id = tray_icon::TrayIconId::new(self.id.to_string());
        let mut builder = TrayIconBuilder::new().with_id(tray_id);

        if let Some(ref tooltip) = payload.tooltip {
            builder = builder.with_tooltip(tooltip);
        }
        if let Some(ref title) = payload.title {
            builder = builder.with_title(title);
        }
        if let Some((ref rgba, w, h)) = payload.icon_rgba {
            match tray_icon::Icon::from_rgba(rgba.clone(), w, h) {
                Ok(icon) => { builder = builder.with_icon(icon); }
                Err(e) => { eprintln!("[wry-native] tray icon from_rgba failed: {}", e); }
            }
        }
        if let Some(ref menu_data) = payload.menu {
            let (muda_menu, live_items) = menu_data.build();
            menu_data.collect_ids(&mut self.menu_item_ids);
            self.live_items = live_items;
            self.live_menu = Some(muda_menu.clone());
            builder = builder.with_menu(Box::new(muda_menu));
        }
        builder = builder.with_menu_on_left_click(payload.menu_on_left_click);
        builder = builder.with_icon_as_template(payload.icon_is_template);

        match builder.build() {
            Ok(tray) => {
                if !payload.visible {
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

    /// Append a newly created item to either the top-level menu (parent_id empty)
    /// or a submenu identified by parent_id.
    fn append_to_parent(&self, parent_id: &str, item: &dyn tray_menu::IsMenuItem) -> bool {
        if parent_id.is_empty() {
            if let Some(ref menu) = self.live_menu {
                return menu.append(item).is_ok();
            }
        } else if let Some(LiveMenuItem::Submenu(ref sub)) = self.live_items.get(parent_id) {
            return sub.append(item).is_ok();
        }
        false
    }

    /// Insert a newly created item at `position` in either the top-level menu
    /// (parent_id empty) or a submenu identified by parent_id.
    fn insert_in_parent(&self, parent_id: &str, position: usize, item: &dyn tray_menu::IsMenuItem) -> bool {
        if parent_id.is_empty() {
            if let Some(ref menu) = self.live_menu {
                return menu.insert(item, position).is_ok();
            }
        } else if let Some(LiveMenuItem::Submenu(ref sub)) = self.live_items.get(parent_id) {
            return sub.insert(item, position).is_ok();
        }
        false
    }

    /// Remove an item by ID. Tries the top-level menu and all submenus.
    fn remove_by_id(&mut self, id: &str) -> bool {
        let item = match self.live_items.get(id) {
            Some(item) => item,
            None => return false,
        };
        let dyn_item = item.as_is_menu_item();
        let mut removed = false;
        if let Some(ref menu) = self.live_menu {
            if menu.remove(dyn_item).is_ok() {
                removed = true;
            }
        }
        if !removed {
            for (k, v) in &self.live_items {
                if k == id { continue; }
                if let LiveMenuItem::Submenu(ref sub) = v {
                    if sub.remove(dyn_item).is_ok() {
                        removed = true;
                        break;
                    }
                }
            }
        }
        if removed {
            // If we removed a submenu, also remove all its children
            if let Some(LiveMenuItem::Submenu(ref sub)) = self.live_items.get(id) {
                let child_ids: Vec<String> = sub.items()
                    .into_iter()
                    .filter_map(|kind| {
                        let mid: &tray_menu::MenuId = match &kind {
                            tray_menu::MenuItemKind::MenuItem(i) => i.id(),
                            tray_menu::MenuItemKind::Check(i) => i.id(),
                            tray_menu::MenuItemKind::Submenu(i) => i.id(),
                            tray_menu::MenuItemKind::Predefined(i) => i.id(),
                            tray_menu::MenuItemKind::Icon(i) => i.id(),
                        };
                        Some(mid.as_ref().to_string())
                    })
                    .collect();
                for cid in &child_ids {
                    self.live_items.remove(cid);
                }
            }
            self.live_items.remove(id);
        }
        removed
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

// ===========================================================================
// EXPORTED C API
// ===========================================================================

// ---------------------------------------------------------------------------
// Tray menu building
// ---------------------------------------------------------------------------

/// Create a new tray menu. Returns an opaque handle.
/// Free with `wry_tray_menu_destroy` if not consumed by a tray create/set call.
#[no_mangle]
pub extern "C" fn wry_tray_menu_new() -> *mut WryTrayMenu {
    Box::into_raw(Box::new(WryTrayMenu { items: Vec::new() }))
}

/// Add a clickable menu item.
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
    id: *const c_char,
    label: *const c_char,
    enabled: bool,
) -> *mut WryTrayMenu {
    if menu.is_null() { return std::ptr::null_mut(); }
    let menu = unsafe { &mut *menu };
    let id = unsafe { c_str_to_string(id) };
    let label = unsafe { c_str_to_string(label) };
    menu.items.push(WryTrayMenuItem::Submenu {
        id,
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

/// Free a tray menu that was NOT consumed by tray creation or set_menu.
/// Do NOT call this on menus already passed to a create/set call,
/// or on submenu pointers returned by `wry_tray_menu_add_submenu`.
#[no_mangle]
pub extern "C" fn wry_tray_menu_destroy(menu: *mut WryTrayMenu) {
    if !menu.is_null() {
        unsafe { drop(Box::from_raw(menu)); }
    }
}

// ---------------------------------------------------------------------------
// Tray creation (create-with-options pattern)
// ---------------------------------------------------------------------------

/// Create a new tray icon with all configuration in one call.
/// Returns an opaque tray ID (>0) on success, 0 on failure.
/// The tray is materialized when `wry_app_run()` is called.
/// The options struct's `menu` field is consumed (ownership transferred).
#[no_mangle]
pub extern "C" fn wry_tray_create(app: *mut WryApp, opts: *const WryTrayCreateOptions) -> usize {
    if app.is_null() || opts.is_null() { return 0; }
    let app = unsafe { &mut *app };
    let opts = unsafe { &*opts };

    let id = app.next_tray_id;
    app.next_tray_id += 1;
    let mut tray = WryTray::new(id);

    if !opts.event_callback.is_null() {
        let cb: TrayEventCallback = unsafe { std::mem::transmute(opts.event_callback) };
        tray.event_handler = Some((cb, opts.event_ctx as usize));
    }
    if !opts.menu_event_callback.is_null() {
        let cb: TrayMenuEventCallback = unsafe { std::mem::transmute(opts.menu_event_callback) };
        tray.menu_event_handler = Some((cb, opts.menu_event_ctx as usize));
    }

    let payload = TrayCreatePayload::from_options(opts);
    app.trays.insert(id, tray);
    app.tray_payloads.insert(id, payload);
    id
}

// ---------------------------------------------------------------------------
// Tray runtime setters (operate on live WryTray pointer)
// ---------------------------------------------------------------------------

/// Set the tray icon from raw RGBA pixel data.
#[no_mangle]
pub extern "C" fn wry_tray_set_icon(
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
            Err(e) => { eprintln!("[wry-native] tray set_icon from_rgba failed: {}", e); }
        }
    }
}

/// Set the tray icon from encoded image file bytes.
#[no_mangle]
pub extern "C" fn wry_tray_set_icon_from_bytes(
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

/// Set the tray tooltip.
#[no_mangle]
pub extern "C" fn wry_tray_set_tooltip(tray: *mut WryTray, tooltip: *const c_char) {
    if tray.is_null() { return; }
    let tray = unsafe { &mut *tray };
    if let Some(ref t) = tray.tray {
        let s = unsafe { c_str_to_string(tooltip) };
        let val: Option<&str> = if s.is_empty() { None } else { Some(&s) };
        log_err!(t.set_tooltip(val), "tray set_tooltip");
    }
}

/// Set the tray title. macOS and Linux only.
#[no_mangle]
pub extern "C" fn wry_tray_set_title(tray: *mut WryTray, title: *const c_char) {
    if tray.is_null() { return; }
    let tray = unsafe { &mut *tray };
    if let Some(ref t) = tray.tray {
        let s = unsafe { c_str_to_string(title) };
        let val: Option<&str> = if s.is_empty() { None } else { Some(&s) };
        t.set_title(val);
    }
}

/// Show or hide the tray icon.
#[no_mangle]
pub extern "C" fn wry_tray_set_visible(tray: *mut WryTray, visible: bool) {
    if tray.is_null() { return; }
    let tray = unsafe { &mut *tray };
    if let Some(ref t) = tray.tray {
        log_err!(t.set_visible(visible), "tray set_visible");
    }
}

/// Replace the tray context menu. Takes ownership of the menu.
#[no_mangle]
pub extern "C" fn wry_tray_set_menu(tray: *mut WryTray, menu: *mut WryTrayMenu) {
    if tray.is_null() { return; }
    let tray = unsafe { &mut *tray };
    if let Some(ref t) = tray.tray {
        if menu.is_null() {
            tray.live_items.clear();
            tray.live_menu = None;
            t.set_menu(None);
        } else {
            let menu_data = unsafe { Box::from_raw(menu) };
            let (muda_menu, live_items) = menu_data.build();
            tray.live_items = live_items;
            tray.live_menu = Some(muda_menu.clone());
            t.set_menu(Some(Box::new(muda_menu)));
        }
    }
}

/// Enable or disable showing the tray menu on left click.
#[no_mangle]
pub extern "C" fn wry_tray_set_menu_on_left_click(tray: *mut WryTray, enable: bool) {
    if tray.is_null() { return; }
    let tray = unsafe { &mut *tray };
    if let Some(ref t) = tray.tray {
        t.set_show_menu_on_left_click(enable);
    }
}

/// Use the icon as a template icon. macOS only.
#[no_mangle]
pub extern "C" fn wry_tray_set_icon_as_template(tray: *mut WryTray, is_template: bool) {
    if tray.is_null() { return; }
    let tray = unsafe { &mut *tray };
    if let Some(ref t) = tray.tray {
        t.set_icon_as_template(is_template);
    }
}

// ---------------------------------------------------------------------------
// Menu item runtime getters/setters (by item string ID)
// ---------------------------------------------------------------------------

/// Get the text of a menu item. Returns null if not found.
/// The caller must free the returned string with `wry_string_free`.
#[no_mangle]
pub extern "C" fn wry_tray_menu_item_text(
    tray: *mut WryTray,
    id: *const c_char,
) -> *mut c_char {
    if tray.is_null() { return std::ptr::null_mut(); }
    let tray = unsafe { &*tray };
    let id = unsafe { c_str_to_string(id) };
    tray.live_items.get(&id)
        .and_then(|mi| CString::new(mi.text()).ok())
        .map_or(std::ptr::null_mut(), |cs| cs.into_raw())
}

/// Set the text of a menu item. No-op if not found.
#[no_mangle]
pub extern "C" fn wry_tray_menu_item_set_text(
    tray: *mut WryTray,
    id: *const c_char,
    text: *const c_char,
) {
    if tray.is_null() { return; }
    let tray = unsafe { &*tray };
    let id = unsafe { c_str_to_string(id) };
    let text = unsafe { c_str_to_string(text) };
    if let Some(mi) = tray.live_items.get(&id) {
        mi.set_text(&text);
    }
}

/// Returns whether a menu item is enabled. Returns false if not found.
#[no_mangle]
pub extern "C" fn wry_tray_menu_item_is_enabled(
    tray: *mut WryTray,
    id: *const c_char,
) -> bool {
    if tray.is_null() { return false; }
    let tray = unsafe { &*tray };
    let id = unsafe { c_str_to_string(id) };
    tray.live_items.get(&id).map_or(false, |mi| mi.is_enabled())
}

/// Enable or disable a menu item. No-op if not found.
#[no_mangle]
pub extern "C" fn wry_tray_menu_item_set_enabled(
    tray: *mut WryTray,
    id: *const c_char,
    enabled: bool,
) {
    if tray.is_null() { return; }
    let tray = unsafe { &*tray };
    let id = unsafe { c_str_to_string(id) };
    if let Some(mi) = tray.live_items.get(&id) {
        mi.set_enabled(enabled);
    }
}

/// Returns whether a check menu item is currently checked.
/// Returns false if the item is not found or is not a check item.
#[no_mangle]
pub extern "C" fn wry_tray_check_item_is_checked(
    tray: *mut WryTray,
    id: *const c_char,
) -> bool {
    if tray.is_null() { return false; }
    let tray = unsafe { &*tray };
    let id = unsafe { c_str_to_string(id) };
    match tray.live_items.get(&id) {
        Some(LiveMenuItem::Check(mi)) => mi.is_checked(),
        _ => false,
    }
}

/// Set the checked state of a check menu item.
/// No-op if the item is not found or is not a check item.
#[no_mangle]
pub extern "C" fn wry_tray_check_item_set_checked(
    tray: *mut WryTray,
    id: *const c_char,
    checked: bool,
) {
    if tray.is_null() { return; }
    let tray = unsafe { &*tray };
    let id = unsafe { c_str_to_string(id) };
    if let Some(LiveMenuItem::Check(mi)) = tray.live_items.get(&id) {
        mi.set_checked(checked);
    }
}

// ---------------------------------------------------------------------------
// Dynamic menu item append / insert / remove
// ---------------------------------------------------------------------------

/// Append a new menu item to a live tray menu.
///
/// - `parent_id`: null or empty = top-level menu; otherwise the ID of a submenu.
/// - `kind`: 0=Item, 1=Check, 2=Submenu, 3=Separator.
/// - `id`, `label`: used for Item/Check/Submenu (ignored for Separator).
/// - `checked`: initial checked state (only for Check, kind=1).
/// - `enabled`: enabled state (ignored for Separator).
#[no_mangle]
pub extern "C" fn wry_tray_menu_item_append(
    tray: *mut WryTray,
    parent_id: *const c_char,
    kind: c_int,
    id: *const c_char,
    label: *const c_char,
    checked: bool,
    enabled: bool,
) {
    if tray.is_null() { return; }
    let tray = unsafe { &mut *tray };
    let parent = unsafe { c_str_to_string(parent_id) };
    let item_id = unsafe { c_str_to_string(id) };
    let item_label = unsafe { c_str_to_string(label) };
    match kind {
        0 => {
            let mi = tray_menu::MenuItem::with_id(item_id.as_str(), &item_label, enabled, None);
            if tray.append_to_parent(&parent, &mi) {
                tray.live_items.insert(item_id, LiveMenuItem::Item(mi));
            }
        }
        1 => {
            let mi = tray_menu::CheckMenuItem::with_id(item_id.as_str(), &item_label, enabled, checked, None);
            if tray.append_to_parent(&parent, &mi) {
                tray.live_items.insert(item_id, LiveMenuItem::Check(mi));
            }
        }
        2 => {
            let sub = tray_menu::Submenu::with_id(item_id.as_str(), &item_label, enabled);
            if tray.append_to_parent(&parent, &sub) {
                tray.live_items.insert(item_id, LiveMenuItem::Submenu(sub));
            }
        }
        3 => {
            let sep = tray_menu::PredefinedMenuItem::separator();
            tray.append_to_parent(&parent, &sep);
        }
        _ => {}
    }
}

/// Insert a new menu item at a position in a live tray menu.
///
/// Parameters are the same as `wry_tray_menu_item_append`, with an added `position` index.
#[no_mangle]
pub extern "C" fn wry_tray_menu_item_insert(
    tray: *mut WryTray,
    parent_id: *const c_char,
    position: c_int,
    kind: c_int,
    id: *const c_char,
    label: *const c_char,
    checked: bool,
    enabled: bool,
) {
    if tray.is_null() { return; }
    let tray = unsafe { &mut *tray };
    let parent = unsafe { c_str_to_string(parent_id) };
    let pos = position.max(0) as usize;
    let item_id = unsafe { c_str_to_string(id) };
    let item_label = unsafe { c_str_to_string(label) };
    match kind {
        0 => {
            let mi = tray_menu::MenuItem::with_id(item_id.as_str(), &item_label, enabled, None);
            if tray.insert_in_parent(&parent, pos, &mi) {
                tray.live_items.insert(item_id, LiveMenuItem::Item(mi));
            }
        }
        1 => {
            let mi = tray_menu::CheckMenuItem::with_id(item_id.as_str(), &item_label, enabled, checked, None);
            if tray.insert_in_parent(&parent, pos, &mi) {
                tray.live_items.insert(item_id, LiveMenuItem::Check(mi));
            }
        }
        2 => {
            let sub = tray_menu::Submenu::with_id(item_id.as_str(), &item_label, enabled);
            if tray.insert_in_parent(&parent, pos, &sub) {
                tray.live_items.insert(item_id, LiveMenuItem::Submenu(sub));
            }
        }
        3 => {
            let sep = tray_menu::PredefinedMenuItem::separator();
            tray.insert_in_parent(&parent, pos, &sep);
        }
        _ => {}
    }
}

/// Remove a menu item by ID from a live tray menu.
/// Searches the top-level menu and all submenus for the item.
/// If the removed item is a submenu, its children are also cleaned up.
#[no_mangle]
pub extern "C" fn wry_tray_menu_item_remove(
    tray: *mut WryTray,
    id: *const c_char,
) {
    if tray.is_null() { return; }
    let tray = unsafe { &mut *tray };
    let id = unsafe { c_str_to_string(id) };
    tray.remove_by_id(&id);
}

/// Remove a menu item at a position from a live tray menu.
///
/// - `parent_id`: null or empty = top-level menu; otherwise the ID of a submenu.
/// - `position`: zero-based index.
#[no_mangle]
pub extern "C" fn wry_tray_menu_item_remove_at(
    tray: *mut WryTray,
    parent_id: *const c_char,
    position: c_int,
) {
    if tray.is_null() { return; }
    let tray = unsafe { &mut *tray };
    let parent = unsafe { c_str_to_string(parent_id) };
    let pos = position.max(0) as usize;

    let removed_kind = if parent.is_empty() {
        tray.live_menu.as_ref().and_then(|m| m.remove_at(pos))
    } else {
        match tray.live_items.get(&parent) {
            Some(LiveMenuItem::Submenu(ref sub)) => sub.remove_at(pos),
            _ => None,
        }
    };

    if let Some(kind) = removed_kind {
        let removed_id: String = match &kind {
            tray_menu::MenuItemKind::MenuItem(i) => i.id().as_ref().to_string(),
            tray_menu::MenuItemKind::Check(i) => i.id().as_ref().to_string(),
            tray_menu::MenuItemKind::Submenu(i) => i.id().as_ref().to_string(),
            tray_menu::MenuItemKind::Predefined(i) => i.id().as_ref().to_string(),
            tray_menu::MenuItemKind::Icon(i) => i.id().as_ref().to_string(),
        };
        tray.live_items.remove(&removed_id);
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
