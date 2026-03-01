using System.Runtime.InteropServices;

namespace Wry.NET;

/// <summary>
/// Raw P/Invoke bindings to the wry_native shared library.
/// All functions map 1:1 to the exported C API.
/// </summary>
internal static partial class NativeMethods
{
    private const string LibName = "wry_native";

    // -----------------------------------------------------------------------
    // App lifecycle
    // -----------------------------------------------------------------------

    [LibraryImport(LibName)]
    internal static partial nint wry_app_new();

    [LibraryImport(LibName)]
    internal static partial void wry_app_run(nint app);

    [LibraryImport(LibName)]
    internal static partial void wry_app_on_exit_requested(nint app, nint callback, nint ctx);

    [LibraryImport(LibName)]
    internal static partial void wry_app_exit(nint app, int code);

    [LibraryImport(LibName)]
    internal static partial void wry_app_destroy(nint app);

    // -----------------------------------------------------------------------
    // Window creation
    // -----------------------------------------------------------------------

    [LibraryImport(LibName)]
    internal static partial nuint wry_window_new(nint app);

    // -----------------------------------------------------------------------
    // Navigation & JS interop (pre-run: app + windowId)
    // -----------------------------------------------------------------------

    [LibraryImport(LibName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void wry_window_load_url(nint app, nuint windowId, string url);

    [LibraryImport(LibName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void wry_window_load_html(nint app, nuint windowId, string html);

    [LibraryImport(LibName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void wry_window_eval_js(nint win, string js);

    [LibraryImport(LibName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void wry_window_add_init_script(nint app, nuint windowId, string js);

    // -----------------------------------------------------------------------
    // Callback registration (pre-run) — function pointers passed as nint
    // -----------------------------------------------------------------------

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_ipc_handler(nint app, nuint windowId, nint callback, nint ctx);

    [LibraryImport(LibName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void wry_window_add_custom_protocol(nint app, nuint windowId, string scheme, nint callback, nint ctx);

    [LibraryImport(LibName)]
    internal static partial void wry_protocol_respond(nint responder, nint data, int dataLen,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string contentType, int statusCode,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? extraHeaders);

    [LibraryImport(LibName)]
    internal static partial void wry_window_on_close(nint app, nuint windowId, nint callback, nint ctx);

    [LibraryImport(LibName)]
    internal static partial void wry_window_on_resize(nint app, nuint windowId, nint callback, nint ctx);

    [LibraryImport(LibName)]
    internal static partial void wry_window_on_move(nint app, nuint windowId, nint callback, nint ctx);

    [LibraryImport(LibName)]
    internal static partial void wry_window_on_focus(nint app, nuint windowId, nint callback, nint ctx);

    [LibraryImport(LibName)]
    internal static partial void wry_window_on_drag_drop(nint app, nuint windowId, nint callback, nint ctx);

    // -----------------------------------------------------------------------
    // Navigation & page load handlers (pre-run)
    // -----------------------------------------------------------------------

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_navigation_handler(nint app, nuint windowId, nint callback, nint ctx);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_page_load_handler(nint app, nuint windowId, nint callback, nint ctx);

    // -----------------------------------------------------------------------
    // Evaluate JS with callback (post-run, via WryWindow pointer)
    // -----------------------------------------------------------------------

    [LibraryImport(LibName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void wry_window_eval_js_callback(nint win, string js, nint callback, nint ctx);

    // -----------------------------------------------------------------------
    // Window property setters (pre-run: app + windowId)
    // -----------------------------------------------------------------------

    [LibraryImport(LibName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void wry_window_set_title(nint app, nuint windowId, string title);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_size(nint app, nuint windowId, int width, int height);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_min_size(nint app, nuint windowId, int width, int height);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_max_size(nint app, nuint windowId, int width, int height);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_position(nint app, nuint windowId, int x, int y);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_resizable(nint app, nuint windowId, [MarshalAs(UnmanagedType.U1)] bool resizable);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_fullscreen(nint app, nuint windowId, [MarshalAs(UnmanagedType.U1)] bool fullscreen);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_maximized(nint app, nuint windowId, [MarshalAs(UnmanagedType.U1)] bool maximized);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_minimized(nint app, nuint windowId, [MarshalAs(UnmanagedType.U1)] bool minimized);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_topmost(nint app, nuint windowId, [MarshalAs(UnmanagedType.U1)] bool topmost);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_visible(nint app, nuint windowId, [MarshalAs(UnmanagedType.U1)] bool visible);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_devtools(nint app, nuint windowId, [MarshalAs(UnmanagedType.U1)] bool enabled);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_transparent(nint app, nuint windowId, [MarshalAs(UnmanagedType.U1)] bool transparent);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_decorations(nint app, nuint windowId, [MarshalAs(UnmanagedType.U1)] bool decorations);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_skip_taskbar(nint app, nuint windowId, [MarshalAs(UnmanagedType.U1)] bool skip);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_content_protected(nint app, nuint windowId, [MarshalAs(UnmanagedType.U1)] bool contentProtected);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_shadow(nint app, nuint windowId, [MarshalAs(UnmanagedType.U1)] bool shadow);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_always_on_bottom(nint app, nuint windowId, [MarshalAs(UnmanagedType.U1)] bool alwaysOnBottom);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_maximizable(nint app, nuint windowId, [MarshalAs(UnmanagedType.U1)] bool maximizable);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_minimizable(nint app, nuint windowId, [MarshalAs(UnmanagedType.U1)] bool minimizable);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_closable(nint app, nuint windowId, [MarshalAs(UnmanagedType.U1)] bool closable);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_focusable(nint app, nuint windowId, [MarshalAs(UnmanagedType.U1)] bool focusable);

    [LibraryImport(LibName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void wry_window_set_window_classname(nint app, nuint windowId, string classname);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_owner_window(nint app, nuint windowId, nuint ownerWindowId);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_parent_window(nint app, nuint windowId, nuint parentWindowId);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_prevent_overflow(nint app, nuint windowId, [MarshalAs(UnmanagedType.U1)] bool enabled);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_prevent_overflow_margin(nint app, nuint windowId, int left, int top, int right, int bottom);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_prevent_overflow_direct(nint win, [MarshalAs(UnmanagedType.U1)] bool enabled);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_prevent_overflow_margin_direct(nint win, int left, int top, int right, int bottom);

    [LibraryImport(LibName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void wry_window_set_user_agent(nint app, nuint windowId, string userAgent);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_zoom(nint app, nuint windowId, double zoom);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_back_forward_gestures(nint app, nuint windowId, [MarshalAs(UnmanagedType.U1)] bool enabled);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_autoplay(nint app, nuint windowId, [MarshalAs(UnmanagedType.U1)] bool enabled);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_hotkeys_zoom(nint app, nuint windowId, [MarshalAs(UnmanagedType.U1)] bool enabled);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_clipboard(nint app, nuint windowId, [MarshalAs(UnmanagedType.U1)] bool enabled);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_accept_first_mouse(nint app, nuint windowId, [MarshalAs(UnmanagedType.U1)] bool enabled);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_incognito(nint app, nuint windowId, [MarshalAs(UnmanagedType.U1)] bool enabled);

    [LibraryImport(LibName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void wry_window_set_data_directory(nint app, nuint windowId, string path);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_icon(nint app, nuint windowId, nint rgba, int rgbaLen, int width, int height);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_icon_direct(nint win, nint rgba, int rgbaLen, int width, int height);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_icon_from_bytes(nint app, nuint windowId, nint data, int dataLen);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_icon_from_bytes_direct(nint win, nint data, int dataLen);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_focused(nint app, nuint windowId, [MarshalAs(UnmanagedType.U1)] bool focused);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_javascript_disabled(nint app, nuint windowId, [MarshalAs(UnmanagedType.U1)] bool disabled);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_background_color(nint app, nuint windowId, byte r, byte g, byte b, byte a);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_background_throttling(nint app, nuint windowId, int policy);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_theme(nint app, nuint windowId, int theme);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_https_scheme(nint app, nuint windowId, [MarshalAs(UnmanagedType.U1)] bool enabled);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_browser_accelerator_keys(nint app, nuint windowId, [MarshalAs(UnmanagedType.U1)] bool enabled);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_default_context_menus(nint app, nuint windowId, [MarshalAs(UnmanagedType.U1)] bool enabled);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_scroll_bar_style(nint app, nuint windowId, int style);

    [LibraryImport(LibName)]
    internal static partial void wry_window_center(nint app, nuint windowId);

    // -----------------------------------------------------------------------
    // Window close (post-run, via WryWindow pointer)
    // -----------------------------------------------------------------------

    [LibraryImport(LibName)]
    internal static partial void wry_window_close(nint win);

    // -----------------------------------------------------------------------
    // Window queries (post-run, via WryWindow pointer)
    // -----------------------------------------------------------------------

    [LibraryImport(LibName)]
    internal static partial void wry_window_get_size(nint win, out int width, out int height);

    [LibraryImport(LibName)]
    internal static partial void wry_window_get_position(nint win, out int x, out int y);

    [LibraryImport(LibName)]
    internal static partial nint wry_window_get_title(nint win);

    [LibraryImport(LibName)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool wry_window_get_resizable(nint win);

    [LibraryImport(LibName)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool wry_window_get_fullscreen(nint win);

    [LibraryImport(LibName)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool wry_window_get_maximized(nint win);

    [LibraryImport(LibName)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool wry_window_get_minimized(nint win);

    [LibraryImport(LibName)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool wry_window_get_visible(nint win);

    [LibraryImport(LibName)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool wry_window_get_decorated(nint win);

    [LibraryImport(LibName)]
    internal static partial double wry_window_get_screen_dpi(nint win);

    [LibraryImport(LibName)]
    internal static partial nint wry_window_get_url(nint win);

    // -----------------------------------------------------------------------
    // Post-run direct setters (via WryWindow pointer from callbacks/dispatch)
    // -----------------------------------------------------------------------

    [LibraryImport(LibName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void wry_window_set_title_direct(nint win, string title);

    [LibraryImport(LibName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void wry_window_load_url_direct(nint win, string url);

    [LibraryImport(LibName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void wry_window_load_html_direct(nint win, string html);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_size_direct(nint win, int width, int height);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_position_direct(nint win, int x, int y);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_decorations_direct(nint win, [MarshalAs(UnmanagedType.U1)] bool decorations);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_skip_taskbar_direct(nint win, [MarshalAs(UnmanagedType.U1)] bool skip);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_content_protected_direct(nint win, [MarshalAs(UnmanagedType.U1)] bool contentProtected);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_shadow_direct(nint win, [MarshalAs(UnmanagedType.U1)] bool shadow);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_always_on_bottom_direct(nint win, [MarshalAs(UnmanagedType.U1)] bool alwaysOnBottom);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_maximizable_direct(nint win, [MarshalAs(UnmanagedType.U1)] bool maximizable);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_minimizable_direct(nint win, [MarshalAs(UnmanagedType.U1)] bool minimizable);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_closable_direct(nint win, [MarshalAs(UnmanagedType.U1)] bool closable);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_focusable_direct(nint win, [MarshalAs(UnmanagedType.U1)] bool focusable);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_zoom_direct(nint win, double zoom);

    [LibraryImport(LibName)]
    internal static partial void wry_window_restore(nint win);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_fullscreen_direct(nint win, [MarshalAs(UnmanagedType.U1)] bool fullscreen);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_maximized_direct(nint win, [MarshalAs(UnmanagedType.U1)] bool maximized);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_minimized_direct(nint win, [MarshalAs(UnmanagedType.U1)] bool minimized);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_topmost_direct(nint win, [MarshalAs(UnmanagedType.U1)] bool topmost);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_visible_direct(nint win, [MarshalAs(UnmanagedType.U1)] bool visible);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_resizable_direct(nint win, [MarshalAs(UnmanagedType.U1)] bool resizable);

    [LibraryImport(LibName)]
    internal static partial void wry_window_center_direct(nint win);

    [LibraryImport(LibName)]
    internal static partial void wry_window_get_all_monitors(nint win, nint callback, nint ctx);

    // -----------------------------------------------------------------------
    // WebView runtime methods (post-run, via WryWindow pointer)
    // -----------------------------------------------------------------------

    [LibraryImport(LibName)]
    internal static partial void wry_window_print(nint win);

    [LibraryImport(LibName)]
    internal static partial void wry_window_reload(nint win);

    [LibraryImport(LibName)]
    internal static partial void wry_window_focus(nint win);

    [LibraryImport(LibName)]
    internal static partial void wry_window_focus_parent(nint win);

    [LibraryImport(LibName)]
    internal static partial void wry_window_clear_all_browsing_data(nint win);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_background_color_direct(nint win, byte r, byte g, byte b, byte a);

    [LibraryImport(LibName)]
    internal static partial void wry_window_open_devtools(nint win);

    [LibraryImport(LibName)]
    internal static partial void wry_window_close_devtools(nint win);

    [LibraryImport(LibName)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool wry_window_is_devtools_open(nint win);

    [LibraryImport(LibName)]
    internal static partial nint wry_webview_version();

    // -----------------------------------------------------------------------
    // Cross-thread dispatch
    // -----------------------------------------------------------------------

    [LibraryImport(LibName)]
    internal static partial void wry_window_dispatch(nint app, nuint windowId, nint callback, nint ctx);

    // -----------------------------------------------------------------------
    // Tray menu building
    // -----------------------------------------------------------------------

    [LibraryImport(LibName)]
    internal static partial nint wry_tray_menu_new();

    [LibraryImport(LibName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void wry_tray_menu_add_item(nint menu, string id, string label, [MarshalAs(UnmanagedType.U1)] bool enabled);

    [LibraryImport(LibName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void wry_tray_menu_add_check_item(nint menu, string id, string label, [MarshalAs(UnmanagedType.U1)] bool @checked, [MarshalAs(UnmanagedType.U1)] bool enabled);

    [LibraryImport(LibName)]
    internal static partial void wry_tray_menu_add_separator(nint menu);

    [LibraryImport(LibName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint wry_tray_menu_add_submenu(nint menu, string label, [MarshalAs(UnmanagedType.U1)] bool enabled);

    [LibraryImport(LibName)]
    internal static partial void wry_tray_menu_destroy(nint menu);

    // -----------------------------------------------------------------------
    // Tray lifecycle (pre-run: app + trayId)
    // -----------------------------------------------------------------------

    [LibraryImport(LibName)]
    internal static partial nuint wry_tray_new(nint app);

    [LibraryImport(LibName)]
    internal static partial void wry_tray_set_icon(nint app, nuint trayId, nint rgba, int rgbaLen, int width, int height);

    [LibraryImport(LibName)]
    internal static partial void wry_tray_set_icon_from_bytes(nint app, nuint trayId, nint data, int dataLen);

    [LibraryImport(LibName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void wry_tray_set_tooltip(nint app, nuint trayId, string tooltip);

    [LibraryImport(LibName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void wry_tray_set_title(nint app, nuint trayId, string title);

    [LibraryImport(LibName)]
    internal static partial void wry_tray_set_menu(nint app, nuint trayId, nint menu);

    [LibraryImport(LibName)]
    internal static partial void wry_tray_set_menu_on_left_click(nint app, nuint trayId, [MarshalAs(UnmanagedType.U1)] bool enable);

    [LibraryImport(LibName)]
    internal static partial void wry_tray_set_visible(nint app, nuint trayId, [MarshalAs(UnmanagedType.U1)] bool visible);

    [LibraryImport(LibName)]
    internal static partial void wry_tray_set_icon_as_template(nint app, nuint trayId, [MarshalAs(UnmanagedType.U1)] bool isTemplate);

    // -----------------------------------------------------------------------
    // Tray callbacks (pre-run)
    // -----------------------------------------------------------------------

    [LibraryImport(LibName)]
    internal static partial void wry_tray_on_event(nint app, nuint trayId, nint callback, nint ctx);

    [LibraryImport(LibName)]
    internal static partial void wry_tray_on_menu_event(nint app, nuint trayId, nint callback, nint ctx);

    // -----------------------------------------------------------------------
    // Tray post-run direct setters (via WryTray pointer from callbacks/dispatch)
    // -----------------------------------------------------------------------

    [LibraryImport(LibName)]
    internal static partial void wry_tray_set_icon_direct(nint tray, nint rgba, int rgbaLen, int width, int height);

    [LibraryImport(LibName)]
    internal static partial void wry_tray_set_icon_from_bytes_direct(nint tray, nint data, int dataLen);

    [LibraryImport(LibName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void wry_tray_set_tooltip_direct(nint tray, string tooltip);

    [LibraryImport(LibName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void wry_tray_set_title_direct(nint tray, string title);

    [LibraryImport(LibName)]
    internal static partial void wry_tray_set_visible_direct(nint tray, [MarshalAs(UnmanagedType.U1)] bool visible);

    [LibraryImport(LibName)]
    internal static partial void wry_tray_set_menu_direct(nint tray, nint menu);

    [LibraryImport(LibName)]
    internal static partial void wry_tray_set_menu_on_left_click_direct(nint tray, [MarshalAs(UnmanagedType.U1)] bool enable);

    [LibraryImport(LibName)]
    internal static partial void wry_tray_set_icon_as_template_direct(nint tray, [MarshalAs(UnmanagedType.U1)] bool isTemplate);

    // -----------------------------------------------------------------------
    // Tray cross-thread dispatch & removal
    // -----------------------------------------------------------------------

    [LibraryImport(LibName)]
    internal static partial void wry_tray_dispatch(nint app, nuint trayId, nint callback, nint ctx);

    [LibraryImport(LibName)]
    internal static partial void wry_tray_remove(nint app, nuint trayId);

    // -----------------------------------------------------------------------
    // Dialog (message, ask, confirm, open, save)
    // -----------------------------------------------------------------------

    [LibraryImport(LibName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint wry_dialog_message(string? title, string? message, int kind, int buttons);

    [LibraryImport(LibName, StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool wry_dialog_ask(string? title, string? message, int kind);

    [LibraryImport(LibName, StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool wry_dialog_confirm(string? title, string? message, int kind);

    [LibraryImport(LibName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint wry_dialog_open(
        string? title,
        string? defaultPath,
        [MarshalAs(UnmanagedType.U1)] bool directory,
        [MarshalAs(UnmanagedType.U1)] bool multiple,
        string? filterName,
        string? filterExtensions);

    [LibraryImport(LibName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint wry_dialog_save(
        string? title,
        string? defaultPath,
        string? filterName,
        string? filterExtensions);

    // -----------------------------------------------------------------------
    // String utility
    // -----------------------------------------------------------------------

    [LibraryImport(LibName)]
    internal static partial void wry_string_free(nint s);

    // -----------------------------------------------------------------------
    // Helpers for reading native strings
    // -----------------------------------------------------------------------

    /// <summary>
    /// Read a native UTF-8 string returned by wry_window_get_title / get_url,
    /// free it with wry_string_free, and return a managed string.
    /// </summary>
    internal static string? ReadAndFreeNativeString(nint ptr)
    {
        if (ptr == 0) return null;
        try
        {
            return Marshal.PtrToStringUTF8(ptr);
        }
        finally
        {
            wry_string_free(ptr);
        }
    }
}
