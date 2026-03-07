# API coverage (wry-native)

This document describes what the **wry-native** C API exposes. Desktop only (Windows, Linux, macOS).

- **wry 0.54** - WebView API (builder options, runtime methods, events); ~60-70% of wry's public API covered below.
- **tao 0.34** - Windowing API (title, size, position, decorations, fullscreen, etc.); most of tao's `Window` and `WindowBuilder` surface area is covered.
- **App lifecycle** - Exit-requested callback, programmatic exit, window-created / window-creation-error / window-destroyed callbacks; dynamic window creation via `wry_window_create` with `WryWindowConfig`.
- **tray-icon 0.21** - System tray icons and context menus; coverage table below.
- **rfd 0.17** - Native dialogs (message, ask, confirm, open, save); coverage table below.

**Covered?** ✓ = yes, ✗ = no.

All window and webview builder options are passed at creation time via `WryWindowConfig` (a flat `#[repr(C)]` struct). Runtime setters operate on a live window pointer.

## Window API (tao 0.34)

| Category | tao API | Covered? | wry-native / Notes |
|----------|---------|:--------:|--------------------|
| **Creation** | `WindowBuilder::new()` | ✓ | `wry_window_create` with `WryWindowConfig`; one tao window per webview |
| **Config** | title | ✓ | `WryWindowConfig.title` |
| **Config** | size (width, height) | ✓ | `WryWindowConfig.width`, `WryWindowConfig.height` |
| **Config** | min/max size | ✓ | `WryWindowConfig.min_width/min_height`, `WryWindowConfig.max_width/max_height` |
| **Config** | position (x, y) | ✓ | `WryWindowConfig.x`, `WryWindowConfig.y` |
| **Config** | center | ✓ | `WryWindowConfig.center` |
| **Config** | resizable | ✓ | `WryWindowConfig.resizable` |
| **Config** | fullscreen | ✓ | `WryWindowConfig.fullscreen` |
| **Config** | maximized | ✓ | `WryWindowConfig.maximized` |
| **Config** | minimized | ✓ | `WryWindowConfig.minimized` |
| **Config** | visible | ✓ | `WryWindowConfig.visible` |
| **Config** | decorations | ✓ | `WryWindowConfig.decorations` |
| **Config** | always_on_top | ✓ | `WryWindowConfig.topmost` |
| **Config** | skip_taskbar | ✓ | `WryWindowConfig.skip_taskbar` |
| **Config** | content_protected | ✓ | `WryWindowConfig.content_protected` |
| **Config** | shadow | ✓ | `WryWindowConfig.shadow` (Win) |
| **Config** | always_on_bottom | ✓ | `WryWindowConfig.always_on_bottom` |
| **Config** | maximizable | ✓ | `WryWindowConfig.maximizable` |
| **Config** | minimizable | ✓ | `WryWindowConfig.minimizable` |
| **Config** | closable | ✓ | `WryWindowConfig.closable` |
| **Config** | focusable | ✓ | `WryWindowConfig.focusable` |
| **Config** | window_classname | ✓ | `WryWindowConfig.window_classname` (Win) |
| **Config** | owner window / parent window | ✓ | `WryWindowConfig.owner_window_id`, `WryWindowConfig.parent_window_id` |
| **Config** | icon | ✓ | `WryWindowConfig.icon_path` |
| **Config (Win)** | theme | ✓ | `WryWindowConfig.theme` (0=Auto, 1=Dark, 2=Light) |
| **Runtime** | title get/set | ✓ | `wry_window_get_title`, `wry_window_set_title` |
| **Runtime** | size get/set | ✓ | `wry_window_get_size`, `wry_window_set_size` |
| **Runtime** | position get/set | ✓ | `wry_window_get_position`, `wry_window_set_position` |
| **Runtime** | min/max size set | ✓ | `wry_window_set_min_size`, `wry_window_set_max_size` |
| **Runtime** | resizable get/set | ✓ | `wry_window_get_resizable`, `wry_window_set_resizable` |
| **Runtime** | fullscreen get/set | ✓ | `wry_window_get_fullscreen`, `wry_window_set_fullscreen` |
| **Runtime** | maximized get/set | ✓ | `wry_window_get_maximized`, `wry_window_set_maximized` |
| **Runtime** | minimized get/set | ✓ | `wry_window_get_minimized`, `wry_window_set_minimized` |
| **Runtime** | visible get/set | ✓ | `wry_window_get_visible`, `wry_window_set_visible` |
| **Runtime** | decorated get/set | ✓ | `wry_window_get_decorated`, `wry_window_set_decorations` |
| **Runtime** | topmost set | ✓ | `wry_window_set_topmost` |
| **Runtime** | skip_taskbar, content_protected, shadow, always_on_bottom set | ✓ | `wry_window_set_*` |
| **Runtime** | maximizable, minimizable, closable, focusable set | ✓ | `wry_window_set_*` |
| **Runtime** | theme get/set | ✓ | `wry_window_get_theme`, `wry_window_set_theme` (Win) |
| **Runtime** | icon set | ✓ | `wry_window_set_icon` (RGBA), `wry_window_set_icon_from_bytes` (encoded image) |
| **Runtime** | close, restore, center | ✓ | `wry_window_close`, `wry_window_restore`, `wry_window_center` |
| **Runtime** | focus | ✓ | `wry_window_focus` |
| **Runtime** | screen DPI | ✓ | `wry_window_get_screen_dpi` |
| **Events** | close / resize / move / focus | ✓ | `WryWindowConfig.*` callback fields |
| **Threading** | (cross-thread dispatch) | ✓ | `wry_window_dispatch` |
| **Utility** | monitors | ✓ | `wry_window_get_all_monitors` |

## WebView API (wry 0.54)

| Category | wry API | Covered? | wry-native / Notes |
|----------|---------|:--------:|--------------------|
| **Config** | `with_url` | ✓ | `WryWindowConfig.url` |
| **Config** | `with_html` | ✓ | `WryWindowConfig.html` |
| **Config** | `with_user_agent` | ✓ | `WryWindowConfig.user_agent` |
| **Config** | `with_initialization_script` | ✓ | `WryWindowConfig.init_scripts` (array of C strings) |
| **Config** | `with_ipc_handler` | ✓ | `WryWindowConfig.ipc_handler` callback |
| **Config** | `with_asynchronous_custom_protocol` | ✓ | `WryWindowConfig.protocols` (scheme + callback array) + `wry_protocol_respond` |
| **Config** | `with_transparent` | ✓ | `WryWindowConfig.transparent` |
| **Config** | `with_devtools` | ✓ | `WryWindowConfig.devtools` |
| **Config** | `with_back_forward_navigation_gestures` | ✓ | `WryWindowConfig.back_forward_gestures` |
| **Config** | `with_background_color` | ✓ | `WryWindowConfig.bg_r/g/b/a` |
| **Config** | `with_autoplay` | ✓ | `WryWindowConfig.autoplay` |
| **Config** | `with_hotkeys_zoom` | ✓ | `WryWindowConfig.hotkeys_zoom` |
| **Config** | `with_clipboard` | ✓ | `WryWindowConfig.clipboard` |
| **Config** | `with_accept_first_mouse` | ✓ | `WryWindowConfig.accept_first_mouse` (macOS) |
| **Config** | `with_incognito` | ✓ | `WryWindowConfig.incognito` |
| **Config** | `with_focused` | ✓ | `WryWindowConfig.focused` |
| **Config** | `with_background_throttling` | ✓ | `WryWindowConfig.background_throttling` (0=Disabled, 1=Suspend, 2=Throttle) |
| **Config** | `with_javascript_disabled` | ✓ | `WryWindowConfig.javascript_disabled` |
| **Config** | `with_navigation_handler` | ✓ | `WryWindowConfig.navigation_handler` callback |
| **Config** | `with_on_page_load_handler` | ✓ | `WryWindowConfig.page_load_handler` callback |
| **Config** | `with_drag_drop_handler` | ✓ | `WryWindowConfig.drag_drop_handler` callback |
| **Config (Win)** | `with_https_scheme` | ✓ | `WryWindowConfig.https_scheme` |
| **Config (Win)** | `with_browser_accelerator_keys` | ✓ | `WryWindowConfig.browser_accelerator_keys` |
| **Config (Win)** | `with_default_context_menus` | ✓ | `WryWindowConfig.default_context_menus` |
| **Config (Win)** | `with_scroll_bar_style` | ✓ | `WryWindowConfig.scroll_bar_style` (0=Default, 1=FluentOverlay) |
| **Runtime** | `evaluate_script` | ✓ | `wry_window_eval_js` (fire-and-forget) |
| **Runtime** | `evaluate_script_with_callback` | ✓ | `wry_window_eval_js_callback` (result via callback) |
| **Runtime** | `url()` | ✓ | `wry_window_get_url` |
| **Runtime** | `load_url` | ✓ | `wry_window_load_url` |
| **Runtime** | `load_html` | ✓ | `wry_window_load_html` |
| **Runtime** | `zoom` | ✓ | `wry_window_set_zoom` |
| **Runtime** | `set_background_color` | ✓ | `wry_window_set_background_color` (RGBA) |
| **Runtime** | `print()` | ✓ | `wry_window_print` |
| **Runtime** | `reload()` | ✓ | `wry_window_reload` |
| **Runtime** | `focus()` | ✓ | `wry_window_focus` |
| **Runtime** | `focus_parent()` | ✓ | `wry_window_focus_parent` |
| **Runtime** | `clear_all_browsing_data` | ✓ | `wry_window_clear_all_browsing_data` |
| **Runtime** | `open_devtools` | ✓ | `wry_window_open_devtools` |
| **Runtime** | `close_devtools` | ✓ | `wry_window_close_devtools` |
| **Runtime** | `is_devtools_open` | ✓ | `wry_window_is_devtools_open` |
| **Runtime** | `webview_version()` | ✓ | `wry_webview_version` (standalone) |
| **Not covered** | `with_id` / WebViewId | ✗ | Not exposed |
| **Not covered** | `with_initialization_script_for_main_only` | ✗ | Single init-script type only; no main vs subframe |
| **Not covered** | `with_url_and_headers` / `with_headers` | ✗ | URL only, no headers |
| **Not covered** | `with_download_started_handler` | ✗ | Not exposed |
| **Not covered** | `with_download_completed_handler` | ✗ | Not exposed |
| **Not covered** | `with_new_window_req_handler` | ✗ | No `window.open` handling |
| **Not covered** | `with_document_title_changed_handler` | ✗ | Not exposed |
| **Not covered** | `with_proxy_config` | ✗ | Not exposed |
| **Not covered** | `with_bounds` | ✗ | Child webview bounds; not exposed |
| **Not covered** | `build_as_child` | ✗ | One full-window webview only |
| **Not covered** | `new_with_web_context` / shared WebContext | ✗ | Not exposed |
| **Not covered (Win)** | `with_additional_browser_args`, `with_browser_extensions_enabled`, `with_extensions_path`, `with_environment` | ✗ | Not exposed |
| **Not covered (macOS)** | `with_traffic_light_inset`, `with_allow_link_preview`, `with_webview_configuration`, data store, etc. | ✗ | None exposed |
| **Not covered (Unix)** | `build_gtk`, `with_related_view`, `with_extensions_path` | ✗ | None exposed |
| **Not covered** | `cookies_for_url`, `cookies`, `set_cookie`, `delete_cookie` | ✗ | Not exposed |
| **Not covered** | `load_url_with_headers` | ✗ | Not exposed |
| **Not covered** | `bounds()`, `set_bounds()` | ✗ | Child/positioning; not exposed |
| **Not covered (Win)** | `controller()`, `environment()`, `webview()`, `set_theme()`, `set_memory_usage_level()`, `reparent()` | ✗ | None exposed |
| **Not covered (Unix)** | `new_gtk()`, `webview()`, `reparent()` | ✗ | None exposed |
| **Not covered (macOS)** | `print_with_options()`, data store APIs, `webview()`, `manager()`, `ns_window()`, `reparent()`, `set_traffic_light_inset()` | ✗ | None exposed |
| **Not covered** | NewWindowResponse, NewWindowOpener, NewWindowFeatures | ✗ | Not exposed |
| **Not covered** | ProxyConfig, ProxyEndpoint | ✗ | Not exposed |
| **Not covered** | WebContext, Rect (first-class), InitializationScript (main-only), MemoryUsageLevel; wry Error/Result | ✗ | Not exposed |

## Dialog API (rfd 0.17)

| Category | API | wry-native |
|----------|-----|------------|
| **Message** | message box with buttons | `wry_dialog_message(title, message, kind, buttons)` - kind: 0=Info, 1=Warning, 2=Error; buttons: 0=Ok, 1=OkCancel, 2=YesNo, 3=YesNoCancel; returns button label (caller frees with `wry_string_free`) |
| **Ask** | Yes/No dialog | `wry_dialog_ask(title, message, kind)` - returns true for Yes, false for No/Cancel |
| **Confirm** | Ok/Cancel dialog | `wry_dialog_confirm(title, message, kind)` - returns true for Ok, false for Cancel |
| **Open** | file or folder picker | `wry_dialog_open(title, default_path, directory, multiple, filter_name, filter_extensions)` - returns path(s) as newline-separated string or null (caller frees with `wry_string_free`) |
| **Save** | save file dialog | `wry_dialog_save(title, default_path, filter_name, filter_extensions)` - returns path or null (caller frees with `wry_string_free`) |

## App lifecycle

| Category | API | wry-native |
|----------|-----|------------|
| **App** | Create / run / destroy | `wry_app_new`, `wry_app_run`, `wry_app_destroy` |
| **App** | Exit requested callback | `wry_app_on_exit_requested` - fires when all windows close or on `wry_app_exit`; callback receives `has_code` + `code`, returns bool (allow/prevent) |
| **App** | Window created callback | `wry_app_on_window_created` - fires when a window is materialized and live; callback receives `ctx`, `window_id`, `window_ptr` |
| **App** | Window creation error callback | `wry_app_on_window_creation_error` - fires when dynamic window creation fails; callback receives `ctx`, `window_id`, `error_message` (UTF-8) |
| **App** | Window destroyed callback | `wry_app_on_window_destroyed` - fires when a window has been destroyed (platform Destroyed event); callback receives `ctx`, `window_id` |
| **App** | Programmatic exit | `wry_app_exit(app, code)` - request exit from any thread; fires exit-requested callback with the code |
| **Window** | Dynamic creation | `wry_window_create` with `WryWindowConfig`; if called on main thread, created synchronously; otherwise queued and created on main thread. Returns 0 on sync creation failure. |

## tray-icon API coverage (tray-icon 0.21)

| Category | tray-icon API | Covered? | wry-native / Notes |
|----------|---------------|:--------:|--------------------|
| **Lifecycle** | `TrayIconBuilder::new()` | ✓ | `wry_tray_new` (pre-run), creates pending tray; materialized at Init |
| **Builder** | `.with_tooltip()` | ✓ | `wry_tray_set_tooltip` / `wry_tray_set_tooltip_direct` |
| **Builder** | `.with_title()` | ✓ | `wry_tray_set_title` / `wry_tray_set_title_direct` (macOS) |
| **Builder** | `.with_icon()` | ✓ | `wry_tray_set_icon` (RGBA) / `wry_tray_set_icon_from_bytes` (encoded image) |
| **Builder** | `.with_menu()` | ✓ | `wry_tray_set_menu` / `wry_tray_set_menu_direct` |
| **Builder** | `.with_icon_as_template()` | ✓ | `wry_tray_set_icon_as_template` / `_direct` (macOS) |
| **Builder** | `.with_menu_on_left_click()` | ✓ | `wry_tray_set_menu_on_left_click` / `_direct` |
| **TrayIcon** | `set_visible()` | ✓ | `wry_tray_set_visible` / `wry_tray_set_visible_direct` |
| **TrayIcon** | (removal) | ✓ | `wry_tray_remove` - removes from event loop, triggers exit check |
| **Events** | `TrayIconEvent` | ✓ | `wry_tray_on_event` - Click, DoubleClick, Enter, Move, Leave with position, icon rect, button, button state |
| **Events** | `MenuEvent` | ✓ | `wry_tray_on_menu_event` - menu item ID string |
| **Threading** | (cross-thread) | ✓ | `wry_tray_dispatch` |
| **Menu** | `Menu::new()` | ✓ | `wry_tray_menu_new` |
| **Menu** | `MenuItem` | ✓ | `wry_tray_menu_add_item(id, label, enabled)` |
| **Menu** | `CheckMenuItem` | ✓ | `wry_tray_menu_add_check_item(id, label, checked, enabled)` |
| **Menu** | `PredefinedMenuItem::separator()` | ✓ | `wry_tray_menu_add_separator` |
| **Menu** | `Submenu` | ✓ | `wry_tray_menu_add_submenu(label, enabled)` - returns submenu pointer |
| **Menu** | (cleanup) | ✓ | `wry_tray_menu_destroy` |
| **Menu** | Accelerators / keyboard shortcuts | ✗ | Not exposed |
| **Menu** | `PredefinedMenuItem` (Copy, Paste, etc.) | ✗ | Only separator exposed |
| **Menu** | `IconMenuItem` | ✗ | Not exposed |
