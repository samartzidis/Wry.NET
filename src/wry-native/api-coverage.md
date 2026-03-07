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
| **Config** | `with_title` | ✓ | `WryWindowConfig.title` |
| **Config** | `with_inner_size` | ✓ | `WryWindowConfig.width`, `WryWindowConfig.height` |
| **Config** | `with_min_inner_size` / `with_max_inner_size` | ✓ | `WryWindowConfig.min_width/min_height`, `WryWindowConfig.max_width/max_height` |
| **Config** | `with_position` | ✓ | `WryWindowConfig.x`, `WryWindowConfig.y` |
| **Config** | (center on primary monitor) | ✓ | `WryWindowConfig.center` |
| **Config** | `with_resizable` | ✓ | `WryWindowConfig.resizable` |
| **Config** | `with_fullscreen` | ✓ | `WryWindowConfig.fullscreen` |
| **Config** | `with_maximized` | ✓ | `WryWindowConfig.maximized` |
| **Config** | `with_minimized` | ✓ | `WryWindowConfig.minimized` |
| **Config** | `with_visible` | ✓ | `WryWindowConfig.visible` |
| **Config** | `with_decorations` | ✓ | `WryWindowConfig.decorations` |
| **Config** | `with_always_on_top` | ✓ | `WryWindowConfig.topmost` |
| **Config** | `with_skip_taskbar` | ✓ | `WryWindowConfig.skip_taskbar` |
| **Config** | `with_content_protection` | ✓ | `WryWindowConfig.content_protected` |
| **Config** | (window shadow) | ✓ | `WryWindowConfig.shadow` (Win) |
| **Config** | `with_always_on_bottom` | ✓ | `WryWindowConfig.always_on_bottom` |
| **Config** | `with_maximizable` | ✓ | `WryWindowConfig.maximizable` |
| **Config** | `with_minimizable` | ✓ | `WryWindowConfig.minimizable` |
| **Config** | `with_closable` | ✓ | `WryWindowConfig.closable` |
| **Config** | `with_focused` | ✓ | `WryWindowConfig.focusable` |
| **Config (Win)** | `with_window_classname` | ✓ | `WryWindowConfig.window_classname` |
| **Config** | `with_owner_window` / `with_parent_window` | ✓ | `WryWindowConfig.owner_window_id`, `WryWindowConfig.parent_window_id` |
| **Config** | `with_window_icon` | ✓ | `WryWindowConfig.icon_path` |
| **Config (Win)** | `with_theme` | ✓ | `WryWindowConfig.theme` (0=Auto, 1=Dark, 2=Light) |
| **Runtime** | `set_title` / `title` | ✓ | `wry_window_get_title`, `wry_window_set_title` |
| **Runtime** | `set_inner_size` / `inner_size` | ✓ | `wry_window_get_size`, `wry_window_set_size` |
| **Runtime** | `set_outer_position` / `outer_position` | ✓ | `wry_window_get_position`, `wry_window_set_position` |
| **Runtime** | `set_min_inner_size` / `set_max_inner_size` | ✓ | `wry_window_set_min_size`, `wry_window_set_max_size` |
| **Runtime** | `set_resizable` / `is_resizable` | ✓ | `wry_window_get_resizable`, `wry_window_set_resizable` |
| **Runtime** | `set_fullscreen` / `fullscreen` | ✓ | `wry_window_get_fullscreen`, `wry_window_set_fullscreen` |
| **Runtime** | `set_maximized` / `is_maximized` | ✓ | `wry_window_get_maximized`, `wry_window_set_maximized` |
| **Runtime** | `set_minimized` / `is_minimized` | ✓ | `wry_window_get_minimized`, `wry_window_set_minimized` |
| **Runtime** | `set_visible` / `is_visible` | ✓ | `wry_window_get_visible`, `wry_window_set_visible` |
| **Runtime** | `set_decorations` / `is_decorated` | ✓ | `wry_window_get_decorated`, `wry_window_set_decorations` |
| **Runtime** | `set_always_on_top` | ✓ | `wry_window_set_topmost` |
| **Runtime** | `set_skip_taskbar` | ✓ | `wry_window_set_skip_taskbar` |
| **Runtime** | `set_content_protection` | ✓ | `wry_window_set_content_protected` |
| **Runtime** | (set shadow) | ✓ | `wry_window_set_shadow` (Win) |
| **Runtime** | `set_always_on_bottom` | ✓ | `wry_window_set_always_on_bottom` |
| **Runtime** | `set_maximizable` / `set_minimizable` / `set_closable` | ✓ | `wry_window_set_maximizable`, `wry_window_set_minimizable`, `wry_window_set_closable` |
| **Runtime** | `set_focusable` | ✓ | `wry_window_set_focusable` |
| **Runtime (Win)** | `set_theme` / `theme` | ✓ | `wry_window_get_theme`, `wry_window_set_theme` |
| **Runtime** | `set_window_icon` | ✓ | `wry_window_set_icon` (RGBA), `wry_window_set_icon_from_bytes` (encoded image) |
| **Runtime** | (close / restore) | ✓ | `wry_window_close`, `wry_window_restore` |
| **Runtime** | (center on primary monitor) | ✓ | `wry_window_center` |
| **Runtime** | `set_focus` | ✓ | `wry_window_focus` |
| **Runtime** | `scale_factor` | ✓ | `wry_window_get_screen_dpi` |
| **Events** | `CloseRequested` / `Resized` / `Moved` / `Focused` | ✓ | `WryWindowConfig.*` callback fields |
| **Events** | `Destroyed` | ✓ | `wry_app_on_window_destroyed` callback |
| **Threading** | (cross-thread via event loop proxy) | ✓ | `wry_window_dispatch` |
| **Utility** | `available_monitors` | ✓ | `wry_window_get_all_monitors` |
| **Not covered** | `with_inner_size_constraints` / `set_inner_size_constraints` | ✗ | WindowSizeConstraints struct; use min/max size instead |
| **Not covered** | `with_transparent` | ✗ | Window-level transparency (different from wry's webview transparency) |
| **Not covered** | `with_visible_on_all_workspaces` / `set_visible_on_all_workspaces` | ✗ | macOS/Linux only |
| **Not covered** | `with_background_color` / `set_background_color` | ✗ | Window background; wry's webview background color is exposed instead |
| **Not covered** | `request_redraw` | ✗ | Not exposed |
| **Not covered** | `inner_position` / `outer_size` | ✗ | Only outer position and inner size exposed |
| **Not covered** | `is_focused` / `is_always_on_top` | ✗ | Getters not exposed |
| **Not covered** | `is_minimizable` / `is_maximizable` / `is_closable` | ✗ | Getters not exposed (setters are) |
| **Not covered** | `set_ime_position` / `ReceivedImeText` | ✗ | IME not exposed |
| **Not covered** | `set_progress_bar` | ✗ | Taskbar progress not exposed |
| **Not covered** | `request_user_attention` | ✗ | Not exposed |
| **Not covered** | Cursor: `set_cursor_icon`, `set_cursor_position`, `set_cursor_grab`, `set_cursor_visible`, `cursor_position`, `set_ignore_cursor_events` | ✗ | None exposed |
| **Not covered** | `drag_window` / `drag_resize_window` | ✗ | Not exposed |
| **Not covered** | `current_monitor` / `primary_monitor` / `monitor_from_point` | ✗ | Only `available_monitors` exposed |
| **Not covered** | Events: `KeyboardInput`, `ModifiersChanged`, `CursorMoved`, `CursorEntered`, `CursorLeft`, `MouseWheel`, `MouseInput` | ✗ | Input events not exposed |
| **Not covered** | Events: `DroppedFile`, `HoveredFile`, `HoveredFileCancelled` | ✗ | File drop via wry's drag-drop handler instead |
| **Not covered** | Events: `Touch`, `TouchpadPressure`, `AxisMotion` | ✗ | Touch/pressure not exposed |
| **Not covered** | Events: `ScaleFactorChanged`, `ThemeChanged`, `DecorationsClick` | ✗ | Not exposed |
| **Not covered (Win)** | `with_menu`, `with_taskbar_icon`, `with_no_redirection_bitmap`, `with_drag_and_drop`, `with_rtl` | ✗ | None exposed |
| **Runtime (Win)** | `set_enable` / (is_enabled) | ✓ | `wry_window_set_enabled`, `wry_window_is_enabled`; for modal: disable owner while dialog open, re-enable before closing |
| **Not covered (Win)** | `hwnd`, `hinstance`, `set_taskbar_icon`, `set_overlay_icon`, `set_undecorated_shadow`, `set_rtl` | ✗ | None exposed |
| **Not covered (macOS)** | `with_movable_by_window_background`, `with_titlebar_transparent`, `with_title_hidden`, `with_titlebar_hidden`, `with_titlebar_buttons_hidden`, `with_fullsize_content_view` | ✗ | None exposed |
| **Not covered (macOS)** | `with_resize_increments`, `with_disallow_hidpi`, `with_has_shadow`, `with_traffic_light_inset`, `with_automatic_window_tabbing`, `with_tabbing_identifier` | ✗ | None exposed |
| **Not covered (macOS)** | `ns_window`, `ns_view`, `simple_fullscreen`, `set_has_shadow`, `set_traffic_light_inset`, `set_is_document_edited`, tabbing, `set_badge_label` | ✗ | None exposed |
| **Not covered (Unix)** | `with_transparent_draw`, `with_double_buffered`, `with_rgba_visual`, `with_app_paintable`, `with_cursor_moved_event`, `with_default_vbox` | ✗ | None exposed |
| **Not covered (Unix)** | `gtk_window`, `default_vbox`, `set_badge_count` | ✗ | None exposed |

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
| **Not covered** | `with_id` | ✗ | WebViewId not exposed |
| **Not covered** | `with_initialization_script_for_main_only` | ✗ | Single init-script type only; no main vs subframe distinction |
| **Not covered** | `with_url_and_headers` / `with_headers` | ✗ | URL only, no custom headers |
| **Not covered** | `with_custom_protocol` (sync) | ✗ | Only async variant (`with_asynchronous_custom_protocol`) is exposed |
| **Not covered** | `with_download_started_handler` | ✗ | Download events not exposed |
| **Not covered** | `with_download_completed_handler` | ✗ | Download events not exposed |
| **Not covered** | `with_new_window_req_handler` | ✗ | No `window.open` handling |
| **Not covered** | `with_document_title_changed_handler` | ✗ | Not exposed |
| **Not covered** | `with_proxy_config` | ✗ | ProxyConfig (HTTP CONNECT, SOCKSv5) not exposed |
| **Not covered** | `with_bounds` / `bounds()` / `set_bounds()` | ✗ | Child webview positioning; one full-window webview only |
| **Not covered** | `build_as_child` / `new_as_child` | ✗ | One full-window webview only |
| **Not covered** | `new_with_web_context` | ✗ | Shared WebContext not exposed |
| **Not covered** | `id()` | ✗ | Runtime webview id getter not exposed |
| **Not covered** | `set_visible` (runtime) | ✗ | Webview-level visibility not exposed (window-level is) |
| **Not covered** | `load_url_with_headers` | ✗ | No custom headers support |
| `wry_window_get_cookies_for_url` / `get_cookies` / `set_cookie` / `delete_cookie` | `cookies_for_url` / `cookies` / `set_cookie` / `delete_cookie` | ✓ | Get/set/delete cookies; getters return JSON array, C# maps to `System.Net.Cookie` |
| **Not covered (Win)** | `with_additional_browser_args` | ✗ | Extra WebView2 args not exposed |
| **Not covered (Win)** | `with_browser_extensions_enabled` / `with_extensions_path` | ✗ | Browser extensions not exposed |
| **Not covered (Win)** | `with_environment` | ✗ | Shared WebView2 environment not exposed |
| **Not covered (Win)** | `controller()` / `environment()` / `webview()` | ✗ | Native WebView2 handles not exposed |
| **Not covered (Win)** | `set_memory_usage_level` | ✗ | Memory usage target not exposed |
| **Not covered (Win)** | `reparent` | ✗ | Not exposed |
| **Not covered (Darwin)** | `with_data_store_identifier` | ✗ | Custom data store not exposed (macOS 14+, iOS 17+) |
| **Not covered (Darwin)** | `with_on_web_content_process_terminate_handler` | ✗ | Web content process crash handler not exposed |
| **Not covered (Darwin)** | `with_allow_link_preview` | ✗ | Link preview on long press not exposed |
| **Not covered (Darwin)** | `fetch_data_store_identifiers` / `remove_data_store` | ✗ | Data store management not exposed |
| **Not covered (macOS)** | `with_webview_configuration` | ✗ | Custom WKWebViewConfiguration not exposed |
| **Not covered (macOS)** | `with_traffic_light_inset` / `set_traffic_light_inset` | ✗ | Traffic light positioning not exposed |
| **Not covered (macOS)** | `print_with_options` | ✗ | Print with extra options not exposed |
| **Not covered (macOS)** | `webview()` / `manager()` / `ns_window()` | ✗ | Native WKWebView handles not exposed |
| **Not covered (macOS)** | `reparent` | ✗ | Not exposed |
| **Not covered (Unix)** | `build_gtk` / `new_gtk` | ✗ | GTK widget building not exposed |
| **Not covered (Unix)** | `with_related_view` | ✗ | Related webview for `window.open` not exposed |
| **Not covered (Unix)** | `with_extensions_path` | ✗ | Browser extensions not exposed |
| **Not covered (Unix)** | `webview()` / `reparent` | ✗ | Native WebKit2GTK handles not exposed |

## Dialog API (rfd 0.17)

All dialog functions accept an optional `win` parameter (pointer to `WryWindow`, null for no parent). When set, the dialog is modal to that window (cross-platform: Windows, macOS, Linux).

| Category | API | wry-native |
|----------|-----|------------|
| **Message** | message box with buttons | `wry_dialog_message(win, title, message, kind, buttons)` - kind: 0=Info, 1=Warning, 2=Error; buttons: 0=Ok, 1=OkCancel, 2=YesNo, 3=YesNoCancel; returns button label (caller frees with `wry_string_free`) |
| **Ask** | Yes/No dialog | `wry_dialog_ask(win, title, message, kind)` - returns true for Yes, false for No/Cancel |
| **Confirm** | Ok/Cancel dialog | `wry_dialog_confirm(win, title, message, kind)` - returns true for Ok, false for Cancel |
| **Open** | file or folder picker | `wry_dialog_open(win, title, default_path, directory, multiple, filter_name, filter_extensions)` - returns path(s) as newline-separated string or null (caller frees with `wry_string_free`) |
| **Save** | save file dialog | `wry_dialog_save(win, title, default_path, filter_name, filter_extensions)` - returns path or null (caller frees with `wry_string_free`) |

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
