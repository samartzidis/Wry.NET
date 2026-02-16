# API coverage (wry-native)

This document describes what the **wry-native** C API exposes. Desktop only (Windows, Linux, macOS).

- **wry 0.54** — WebView/window API; ~60–70% of wry’s public API covered below.
- **App lifecycle** — Exit-requested callback and programmatic exit.
- **tray-icon 0.21** — System tray icons and context menus; coverage table below.

**Covered?** ✓ = yes, ✗ = no.

| Category | wry API | Covered? | wry-native / Notes |
|----------|---------|:--------:|--------------------|
| **Window** | (one window per tao window) | ✓ | `wry_window_new`; configure before run, then callbacks/dispatch |
| **Builder** | `with_url` | ✓ | `wry_window_load_url` |
| **Builder** | `with_html` | ✓ | `wry_window_load_html` |
| **Builder** | `with_user_agent` | ✓ | `wry_window_set_user_agent` |
| **Builder** | `with_initialization_script` | ✓ | `wry_window_add_init_script` |
| **Builder** | `with_ipc_handler` | ✓ | `wry_window_set_ipc_handler` |
| **Builder** | `with_asynchronous_custom_protocol` | ✓ | `wry_window_add_custom_protocol` + `wry_protocol_respond` |
| **Builder** | `with_transparent` | ✓ | `wry_window_set_transparent` |
| **Builder** | `with_devtools` | ✓ | `wry_window_set_devtools` |
| **Builder** | `with_visible` | ✓ | `wry_window_set_visible` |
| **Builder** | (title, size, min/max, position, resizable, fullscreen, maximized, minimized, topmost, decorations, zoom, center) | ✓ | `wry_window_set_*` (pre-run), `wry_window_set_*_direct` / getters (post-run) |
| **Builder** | `with_back_forward_navigation_gestures` | ✓ | `wry_window_set_back_forward_gestures` |
| **Builder** | `with_background_color` | ✓ | `wry_window_set_background_color` (pre-run) |
| **Builder** | `with_autoplay` | ✓ | `wry_window_set_autoplay` |
| **Builder** | `with_hotkeys_zoom` | ✓ | `wry_window_set_hotkeys_zoom` |
| **Builder** | `with_clipboard` | ✓ | `wry_window_set_clipboard` |
| **Builder** | `with_accept_first_mouse` | ✓ | `wry_window_set_accept_first_mouse` (macOS) |
| **Builder** | `with_incognito` | ✓ | `wry_window_set_incognito` |
| **Builder** | `with_focused` | ✓ | `wry_window_set_focused` |
| **Builder** | `with_background_throttling` | ✓ | `wry_window_set_background_throttling` (0=Disabled, 1=Suspend, 2=Throttle) |
| **Builder** | `with_javascript_disabled` | ✓ | `wry_window_set_javascript_disabled` |
| **WebView** | `evaluate_script` | ✓ | `wry_window_eval_js` (fire-and-forget; use `wry_window_eval_js_callback` for result) |
| **WebView** | `url()` | ✓ | `wry_window_get_url` |
| **WebView** | `load_url` | ✓ | `wry_window_load_url` / `wry_window_load_url_direct` |
| **WebView** | `load_html` | ✓ | `wry_window_load_html` / `wry_window_load_html_direct` |
| **WebView** | `zoom` | ✓ | `wry_window_set_zoom` / `wry_window_set_zoom_direct` |
| **WebView** | `set_visible` | ✓ | `wry_window_set_visible_direct` |
| **WebView** | `print()` | ✓ | `wry_window_print` |
| **WebView** | `reload()` | ✓ | `wry_window_reload` |
| **WebView** | `focus()` | ✓ | `wry_window_focus` |
| **WebView** | `focus_parent()` | ✓ | `wry_window_focus_parent` |
| **WebView** | `clear_all_browsing_data` | ✓ | `wry_window_clear_all_browsing_data` |
| **WebView** | `set_background_color` | ✓ | `wry_window_set_background_color_direct` (RGBA) |
| **WebView** | `open_devtools` | ✓ | `wry_window_open_devtools` |
| **WebView** | `close_devtools` | ✓ | `wry_window_close_devtools` |
| **WebView** | `is_devtools_open` | ✓ | `wry_window_is_devtools_open` |
| **WebView** | `webview_version()` | ✓ | `wry_webview_version` (standalone) |
| **Events** | close / resize / move / focus | ✓ | `wry_window_on_close`, `on_resize`, `on_move`, `on_focus` |
| **Threading** | (cross-thread) | ✓ | `wry_window_dispatch` |
| **Utility** | (monitors) | ✓ | `wry_window_get_all_monitors` |
| **Post-run** | close, restore; window state getters/setters | ✓ | `wry_window_close`, `wry_window_restore`; direct setters and getters |
| **Builder (Win)** | `with_theme` | ✓ | `wry_window_set_theme` (0=Auto, 1=Dark, 2=Light) |
| **Builder (Win)** | `with_https_scheme` | ✓ | `wry_window_set_https_scheme` |
| **Builder (Win)** | `with_browser_accelerator_keys` | ✓ | `wry_window_set_browser_accelerator_keys` |
| **Builder (Win)** | `with_default_context_menus` | ✓ | `wry_window_set_default_context_menus` |
| **Builder (Win)** | `with_scroll_bar_style` | ✓ | `wry_window_set_scroll_bar_style` (0=Default, 1=FluentOverlay) |
| **Builder** | `with_id` / WebViewId | ✗ | Not exposed |
| **Builder** | `with_initialization_script_for_main_only` | ✗ | Single init-script type only; no main vs subframe |
| **Builder** | `with_url_and_headers` / `with_headers` | ✗ | URL only, no headers |
| **Builder** | `with_navigation_handler` | ✓ | `wry_window_set_navigation_handler` — return true/false to allow/block |
| **Builder** | `with_download_started_handler` | ✗ | Not exposed |
| **Builder** | `with_download_completed_handler` | ✗ | Not exposed |
| **Builder** | `with_new_window_req_handler` | ✗ | No `window.open` handling |
| **Builder** | `with_document_title_changed_handler` | ✗ | Not exposed |
| **Builder** | `with_on_page_load_handler` | ✓ | `wry_window_set_page_load_handler` — event 0=Started, 1=Finished |
| **Builder** | `with_proxy_config` | ✗ | Not exposed |
| **Builder** | `with_bounds` | ✗ | Child webview bounds; not exposed |
| **Builder** | `build_as_child` | ✗ | One full-window webview only |
| **Builder** | `with_drag_drop_handler` | ✓ | `wry_window_on_drag_drop` — event types: Enter/Over/Drop/Leave |
| **Builder** | `new_with_web_context` / shared WebContext | ✗ | Not exposed |
| **Builder (Win)** | `with_additional_browser_args`, `with_browser_extensions_enabled`, `with_extensions_path`, `with_environment` | ✗ | Not exposed |
| **Builder (macOS)** | `with_traffic_light_inset`, `with_allow_link_preview`, `with_webview_configuration`, data store, etc. | ✗ | None exposed |
| **Builder (Unix)** | `build_gtk`, `with_related_view`, `with_extensions_path` | ✗ | None exposed |
| **WebView** | `evaluate_script_with_callback` | ✓ | `wry_window_eval_js_callback` — result via callback |
| **WebView** | `cookies_for_url`, `cookies`, `set_cookie`, `delete_cookie` | ✗ | Not exposed |
| **WebView** | `load_url_with_headers` | ✗ | Not exposed |
| **WebView** | `bounds()`, `set_bounds()` | ✗ | Child/positioning; not exposed |
| **WebView (Win)** | `controller()`, `environment()`, `webview()`, `set_theme()`, `set_memory_usage_level()`, `reparent()` | ✗ | None exposed |
| **WebView (Unix)** | `new_gtk()`, `webview()`, `reparent()` | ✗ | None exposed |
| **WebView (macOS)** | `print_with_options()`, data store APIs, `webview()`, `manager()`, `ns_window()`, `reparent()`, `set_traffic_light_inset()` | ✗ | None exposed |
| **Types** | NewWindowResponse, NewWindowOpener, NewWindowFeatures | ✗ | Not exposed |
| **Types** | DragDropEvent, custom handler | ✓ | Exposed via `wry_window_on_drag_drop` callback |
| **Types** | PageLoadEvent, on_page_load_handler | ✓ | Exposed via `wry_window_set_page_load_handler` |
| **Types** | ProxyConfig, ProxyEndpoint | ✗ | Not exposed |
| **Types** | WebContext, Rect (first-class), InitializationScript (main-only), MemoryUsageLevel; wry Error/Result | ✗ | Not exposed |

## App lifecycle

| Category | API | wry-native |
|----------|-----|------------|
| **App** | Create / run / destroy | `wry_app_new`, `wry_app_run`, `wry_app_destroy` |
| **App** | Exit requested callback | `wry_app_on_exit_requested` - fires when all windows close or on `wry_app_exit`; callback receives `has_code` + `code`, returns bool (allow/prevent) |
| **App** | Programmatic exit | `wry_app_exit(app, code)` - request exit from any thread; fires exit-requested callback with the code |

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

**Counts (desktop, approximate)**

| Layer | wry | wry-native |
|-------|-----|------------|
| WebViewBuilder methods | ~35 | ~28 |
| WebView methods | ~25 | ~20 |
| Platform extension traits (Windows) | ~9 | 5 |
| Platform extension traits (macOS/Linux) | Many | 0 |
| tray-icon builder/methods | ~12 | ~12 |
| tray-icon menu items | ~6 | 4 |
