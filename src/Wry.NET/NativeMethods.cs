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
    internal static partial void wry_app_on_window_created(nint app, nint callback, nint ctx);

    [LibraryImport(LibName)]
    internal static partial void wry_app_on_window_creation_error(nint app, nint callback, nint ctx);

    [LibraryImport(LibName)]
    internal static partial void wry_app_on_window_destroyed(nint app, nint callback, nint ctx);

    [LibraryImport(LibName)]
    internal static partial void wry_app_exit(nint app, int code);

    [LibraryImport(LibName)]
    internal static partial void wry_app_destroy(nint app);

    // -----------------------------------------------------------------------
    // Window creation
    // -----------------------------------------------------------------------

    /// <summary>
    /// One protocol handler entry for WryWindowConfig. Layout must match Rust WryProtocolEntry.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct WryProtocolEntryNative
    {
        internal nint Scheme;   // UTF-8, must stay valid for duration of wry_window_create
        internal nint Callback; // ProtocolHandlerCallback function pointer
        internal nint Context;
    }

    /// <summary>
    /// Native config struct for wry_window_create. Layout must match Rust WryWindowConfig (repr C).
    /// String fields are UTF-8 pointers; null = not set. Caller must pin/allocate string memory for the duration of the call.
    /// Protocols: pointer to array of WryProtocolEntryNative; use null if ProtocolCount is 0.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct WryWindowConfigNative
    {
        internal nint Title;
        internal nint Url;
        internal nint Html;
        internal int Width;
        internal int Height;
        internal nint DataDirectory;
        internal int ProtocolCount;
        internal nint Protocols;
        internal int DefaultContextMenus;
        internal nint IconData;
        internal int IconDataLen;
        internal int InitScriptCount;
        internal nint InitScripts;

        // Window properties
        internal int MinWidth;
        internal int MinHeight;
        internal int MaxWidth;
        internal int MaxHeight;
        internal int HasPosition;
        internal int X;
        internal int Y;
        internal int Resizable;
        internal int Fullscreen;
        internal int Maximized;
        internal int Minimized;
        internal int Topmost;
        internal int Visible;
        internal int Devtools;
        internal int Transparent;
        internal int Decorations;
        internal nint UserAgent;
        internal double Zoom;
        internal int BackForwardGestures;
        internal int Autoplay;
        internal int HotkeysZoom;
        internal int Clipboard;
        internal int AcceptFirstMouse;
        internal int Incognito;
        internal int Focused;
        internal int JavascriptDisabled;
        internal int HasBackgroundColor;
        internal byte BgR;
        internal byte BgG;
        internal byte BgB;
        internal byte BgA;
        internal int HasBackgroundThrottling;
        internal int BackgroundThrottling;
        internal int Theme;
        internal int HttpsScheme;
        internal int BrowserAcceleratorKeys;
        internal int ScrollBarStyle;
        internal int SkipTaskbar;
        internal int ContentProtected;
        internal int Shadow;
        internal int AlwaysOnBottom;
        internal int Maximizable;
        internal int Minimizable;
        internal int Closable;
        internal int Focusable;
        internal nint WindowClassname;
        internal nuint OwnerWindowId;
        internal nuint ParentWindowId;

        // Event callbacks: function pointer + opaque context. 0 = not set.
        internal nint IpcHandler;
        internal nint IpcHandlerCtx;
        internal nint CloseHandler;
        internal nint CloseHandlerCtx;
        internal nint ResizeHandler;
        internal nint ResizeHandlerCtx;
        internal nint MoveHandler;
        internal nint MoveHandlerCtx;
        internal nint FocusHandler;
        internal nint FocusHandlerCtx;
        internal nint NavigationHandler;
        internal nint NavigationHandlerCtx;
        internal nint PageLoadHandler;
        internal nint PageLoadHandlerCtx;
        internal nint DragDropHandler;
        internal nint DragDropHandlerCtx;
    }

    /// <summary>
    /// Create a window with optional config. config=null uses defaults.
    /// ownerWindowId and parentWindowId: pass 0 for none; owner takes precedence if both set.
    /// </summary>
    [LibraryImport(LibName)]
    internal static partial nuint wry_window_create(nint app, nuint ownerWindowId, nuint parentWindowId, nint config);

    // -----------------------------------------------------------------------
    // Navigation & JS interop (post-run: use *mut WryWindow)
    // -----------------------------------------------------------------------

    [LibraryImport(LibName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void wry_window_eval_js(nint win, string js);

    // -----------------------------------------------------------------------
    // Protocol response (post-run)
    // -----------------------------------------------------------------------

    [LibraryImport(LibName)]
    internal static partial void wry_protocol_respond(nint responder, nint data, int dataLen,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string contentType, int statusCode,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? extraHeaders);

    // -----------------------------------------------------------------------
    // Evaluate JS with callback (post-run, via WryWindow pointer)
    // -----------------------------------------------------------------------

    [LibraryImport(LibName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void wry_window_eval_js_callback(nint win, string js, nint callback, nint ctx);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_icon(nint win, nint rgba, int rgbaLen, int width, int height);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_icon_from_bytes(nint win, nint data, int dataLen);

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
    internal static partial int wry_window_get_theme(nint win);

    [LibraryImport(LibName)]
    internal static partial double wry_window_get_screen_dpi(nint win);

    [LibraryImport(LibName)]
    internal static partial nint wry_window_get_url(nint win);

    // -----------------------------------------------------------------------
    // Post-run direct setters (via WryWindow pointer from callbacks/dispatch)
    // -----------------------------------------------------------------------

    [LibraryImport(LibName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void wry_window_set_title(nint win, string title);

    [LibraryImport(LibName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void wry_window_load_url(nint win, string url);

    [LibraryImport(LibName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void wry_window_load_html(nint win, string html);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_size(nint win, int width, int height);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_position(nint win, int x, int y);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_min_size(nint win, int width, int height);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_max_size(nint win, int width, int height);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_theme(nint win, int theme);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_decorations(nint win, [MarshalAs(UnmanagedType.U1)] bool decorations);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_skip_taskbar(nint win, [MarshalAs(UnmanagedType.U1)] bool skip);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_content_protected(nint win, [MarshalAs(UnmanagedType.U1)] bool contentProtected);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_shadow(nint win, [MarshalAs(UnmanagedType.U1)] bool shadow);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_always_on_bottom(nint win, [MarshalAs(UnmanagedType.U1)] bool alwaysOnBottom);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_maximizable(nint win, [MarshalAs(UnmanagedType.U1)] bool maximizable);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_minimizable(nint win, [MarshalAs(UnmanagedType.U1)] bool minimizable);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_closable(nint win, [MarshalAs(UnmanagedType.U1)] bool closable);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_focusable(nint win, [MarshalAs(UnmanagedType.U1)] bool focusable);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_enabled(nint win, [MarshalAs(UnmanagedType.U1)] bool enabled);

    [LibraryImport(LibName)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool wry_window_is_enabled(nint win);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_zoom(nint win, double zoom);

    [LibraryImport(LibName)]
    internal static partial void wry_window_restore(nint win);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_fullscreen(nint win, [MarshalAs(UnmanagedType.U1)] bool fullscreen);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_maximized(nint win, [MarshalAs(UnmanagedType.U1)] bool maximized);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_minimized(nint win, [MarshalAs(UnmanagedType.U1)] bool minimized);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_topmost(nint win, [MarshalAs(UnmanagedType.U1)] bool topmost);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_visible(nint win, [MarshalAs(UnmanagedType.U1)] bool visible);

    [LibraryImport(LibName)]
    internal static partial void wry_window_set_resizable(nint win, [MarshalAs(UnmanagedType.U1)] bool resizable);

    [LibraryImport(LibName)]
    internal static partial void wry_window_center(nint win);

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
    internal static partial void wry_window_set_background_color(nint win, byte r, byte g, byte b, byte a);

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
    // Windows native window handles (HWND, HINSTANCE)
    // -----------------------------------------------------------------------

    [LibraryImport(LibName)]
    internal static partial nint wry_window_get_hwnd(nint win);

    [LibraryImport(LibName)]
    internal static partial nint wry_window_get_hinstance(nint win);

    // -----------------------------------------------------------------------
    // WebView2 native handles (Windows only)
    // -----------------------------------------------------------------------

    [LibraryImport(LibName)]
    internal static partial nint wry_window_get_webview2_controller(nint win);

    [LibraryImport(LibName)]
    internal static partial nint wry_window_get_webview2_environment(nint win);

    [LibraryImport(LibName)]
    internal static partial nint wry_window_get_webview2_webview(nint win);

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
    internal static partial nint wry_tray_menu_add_submenu(nint menu, string id, string label, [MarshalAs(UnmanagedType.U1)] bool enabled);

    [LibraryImport(LibName)]
    internal static partial void wry_tray_menu_destroy(nint menu);

    // -----------------------------------------------------------------------
    // Tray creation (create-with-options)
    // -----------------------------------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    internal struct WryTrayCreateOptionsNative
    {
        internal nint Tooltip;
        internal nint Title;
        internal nint IconData;
        internal int IconDataLen;
        internal nint Menu;
        internal int MenuOnLeftClick;
        internal int Visible;
        internal int IconIsTemplate;
        internal nint EventCallback;
        internal nint EventCtx;
        internal nint MenuEventCallback;
        internal nint MenuEventCtx;
    }

    [LibraryImport(LibName)]
    internal static partial nuint wry_tray_create(nint app, nint opts);

    // -----------------------------------------------------------------------
    // Tray runtime setters (operate on live WryTray pointer)
    // -----------------------------------------------------------------------

    [LibraryImport(LibName)]
    internal static partial void wry_tray_set_icon(nint tray, nint rgba, int rgbaLen, int width, int height);

    [LibraryImport(LibName)]
    internal static partial void wry_tray_set_icon_from_bytes(nint tray, nint data, int dataLen);

    [LibraryImport(LibName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void wry_tray_set_tooltip(nint tray, string tooltip);

    [LibraryImport(LibName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void wry_tray_set_title(nint tray, string title);

    [LibraryImport(LibName)]
    internal static partial void wry_tray_set_menu(nint tray, nint menu);

    [LibraryImport(LibName)]
    internal static partial void wry_tray_set_menu_on_left_click(nint tray, [MarshalAs(UnmanagedType.U1)] bool enable);

    [LibraryImport(LibName)]
    internal static partial void wry_tray_set_visible(nint tray, [MarshalAs(UnmanagedType.U1)] bool visible);

    [LibraryImport(LibName)]
    internal static partial void wry_tray_set_icon_as_template(nint tray, [MarshalAs(UnmanagedType.U1)] bool isTemplate);

    // -----------------------------------------------------------------------
    // Tray menu item runtime getters/setters (by item string ID)
    // -----------------------------------------------------------------------

    [LibraryImport(LibName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint wry_tray_menu_item_text(nint tray, string id);

    [LibraryImport(LibName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void wry_tray_menu_item_set_text(nint tray, string id, string text);

    [LibraryImport(LibName, StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool wry_tray_menu_item_is_enabled(nint tray, string id);

    [LibraryImport(LibName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void wry_tray_menu_item_set_enabled(nint tray, string id, [MarshalAs(UnmanagedType.U1)] bool enabled);

    [LibraryImport(LibName, StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool wry_tray_check_item_is_checked(nint tray, string id);

    [LibraryImport(LibName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void wry_tray_check_item_set_checked(nint tray, string id, [MarshalAs(UnmanagedType.U1)] bool @checked);

    // -----------------------------------------------------------------------
    // Tray dynamic menu item append / insert / remove
    // -----------------------------------------------------------------------

    [LibraryImport(LibName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void wry_tray_menu_item_append(
        nint tray, string? parentId, int kind, string? id, string? label,
        [MarshalAs(UnmanagedType.U1)] bool @checked, [MarshalAs(UnmanagedType.U1)] bool enabled);

    [LibraryImport(LibName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void wry_tray_menu_item_insert(
        nint tray, string? parentId, int position, int kind, string? id, string? label,
        [MarshalAs(UnmanagedType.U1)] bool @checked, [MarshalAs(UnmanagedType.U1)] bool enabled);

    [LibraryImport(LibName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void wry_tray_menu_item_remove(nint tray, string id);

    [LibraryImport(LibName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void wry_tray_menu_item_remove_at(nint tray, string? parentId, int position);

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
    internal static partial nint wry_dialog_message(nint win, string? title, string? message, int kind, int buttons);

    [LibraryImport(LibName, StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool wry_dialog_ask(nint win, string? title, string? message, int kind);

    [LibraryImport(LibName, StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool wry_dialog_confirm(nint win, string? title, string? message, int kind);

    [LibraryImport(LibName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint wry_dialog_open(
        nint win,
        string? title,
        string? defaultPath,
        [MarshalAs(UnmanagedType.U1)] bool directory,
        [MarshalAs(UnmanagedType.U1)] bool multiple,
        string? filterName,
        string? filterExtensions);

    [LibraryImport(LibName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint wry_dialog_save(
        nint win,
        string? title,
        string? defaultPath,
        string? filterName,
        string? filterExtensions);

    // -----------------------------------------------------------------------
    // Cookies
    // -----------------------------------------------------------------------

    [LibraryImport(LibName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint wry_window_get_cookies_for_url(nint win, string url);

    [LibraryImport(LibName)]
    internal static partial nint wry_window_get_cookies(nint win);

    [LibraryImport(LibName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void wry_window_set_cookie(nint win,
        string name, string value, string? domain, string? path,
        [MarshalAs(UnmanagedType.U1)] bool secure,
        [MarshalAs(UnmanagedType.U1)] bool httpOnly,
        double expires);

    [LibraryImport(LibName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void wry_window_delete_cookie(nint win,
        string name, string value, string? domain, string? path);

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
