using System.Net;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Wry.NET;

/// <summary>
/// A native webview window. Configure properties before calling
/// <see cref="WryApp.Run"/>. Post-run operations (from events or dispatch)
/// are available after the event loop starts.
/// </summary>
public sealed class WryWindow
{
    private readonly WryApp _app;
    private nuint _windowId;
    private nint _nativePtr; // set once the window is materialized in the event loop
    private GCHandle _gcHandle;
    private List<GCHandle>? _pinnedProtocolHandles; // keeps protocol handler delegates alive when set at create time
    private bool _preventOverflow;
    private (int Left, int Top, int Right, int Bottom) _preventOverflowMargin;

    /// <summary>Function pointer to ProtocolBridge for use when passing protocols in WryWindowCreateOptions.</summary>
    internal static unsafe nint GetProtocolBridgePointer()
    {
        delegate* unmanaged[Cdecl]<nint, nint, nint, nint, int, nint, nint, void> fp = &ProtocolBridge;
        return (nint)fp;
    }

    /// <summary>Stores GCHandles for protocol handlers passed at create time so delegates stay pinned.</summary>
    internal void AddPinnedProtocolHandles(List<GCHandle> handles)
    {
        _pinnedProtocolHandles ??= [];
        _pinnedProtocolHandles.AddRange(handles);
    }

    internal WryWindow(WryApp app)
    {
        _app = app;
        _gcHandle = GCHandle.Alloc(this);
    }

    internal void SetWindowId(nuint id) => _windowId = id;

    /// <summary>Populate callback function pointers and context on a config struct.</summary>
    internal static unsafe void PopulateCallbacks(ref NativeMethods.WryWindowConfigNative config, nint ctx)
    {
        delegate* unmanaged[Cdecl]<nint, nint, nint, void> ipcFp = &IpcBridge;
        delegate* unmanaged[Cdecl]<nint, byte> closeFp = &CloseBridge;
        delegate* unmanaged[Cdecl]<int, int, nint, void> resizeFp = &ResizeBridge;
        delegate* unmanaged[Cdecl]<int, int, nint, void> moveFp = &MoveBridge;
        delegate* unmanaged[Cdecl]<byte, nint, void> focusFp = &FocusBridge;
        delegate* unmanaged[Cdecl]<nint, nint, byte> navFp = &NavigationBridge;
        delegate* unmanaged[Cdecl]<int, nint, nint, void> plFp = &PageLoadBridge;
        delegate* unmanaged[Cdecl]<int, nint, int, int, int, nint, byte> ddFp = &DragDropBridge;

        config.IpcHandler = (nint)ipcFp;
        config.IpcHandlerCtx = ctx;
        config.CloseHandler = (nint)closeFp;
        config.CloseHandlerCtx = ctx;
        config.ResizeHandler = (nint)resizeFp;
        config.ResizeHandlerCtx = ctx;
        config.MoveHandler = (nint)moveFp;
        config.MoveHandlerCtx = ctx;
        config.FocusHandler = (nint)focusFp;
        config.FocusHandlerCtx = ctx;
        config.NavigationHandler = (nint)navFp;
        config.NavigationHandlerCtx = ctx;
        config.PageLoadHandler = (nint)plFp;
        config.PageLoadHandlerCtx = ctx;
        config.DragDropHandler = (nint)ddFp;
        config.DragDropHandlerCtx = ctx;
    }

    internal nint GCHandlePtr => GCHandle.ToIntPtr(_gcHandle);

    /// <summary>Whether the native window has been materialized (post-run).</summary>
    public bool IsLive => _nativePtr != 0;

    /// <summary>Window id (assigned at creation). Use for OwnerWindowId / ParentWindowId on another window.</summary>
    public nuint Id => _windowId;

    // =======================================================================
    // Events
    // =======================================================================

    /// <summary>Raised when an IPC message is received from JavaScript.</summary>
    public event EventHandler<IpcMessageEventArgs>? IpcMessageReceived;

    /// <summary>Raised when the window close is requested. Set Cancel=true to prevent.</summary>
    public event EventHandler<CloseRequestedEventArgs>? CloseRequested;

    /// <summary>Raised when the window is resized.</summary>
    public event EventHandler<SizeChangedEventArgs>? Resized;

    /// <summary>Raised when the window is moved.</summary>
    public event EventHandler<PositionChangedEventArgs>? Moved;

    /// <summary>Raised when the window gains or loses focus.</summary>
    public event EventHandler<FocusChangedEventArgs>? FocusChanged;

    /// <summary>
    /// Raised before the webview navigates to a new URL. Set Cancel=true to block.
    /// </summary>
    public event EventHandler<NavigatingEventArgs>? Navigating;

    /// <summary>
    /// Raised when a page starts or finishes loading.
    /// </summary>
    public event EventHandler<PageLoadEventArgs>? PageLoad;

    /// <summary>
    /// Raised when files are dragged over, dropped onto, or leave the webview.
    /// Set <see cref="DragDropEventArgs.BlockDefault"/> to true to suppress
    /// the OS default behavior.
    /// </summary>
    public event EventHandler<DragDropEventArgs>? DragDrop;

    /// <summary>
    /// Raised when this window has been destroyed (platform Destroyed event - e.g. user closed it or OS destroyed it with its owner).
    /// </summary>
    public event EventHandler<EventArgs>? WindowDestroyed;

    // =======================================================================
    // Properties (set before app.Run() to configure; getters available post-run)
    // =======================================================================

    /// <summary>Get or set the window title.</summary>
    public string? Title
    {
        get => NativeMethods.ReadAndFreeNativeString(NativeMethods.wry_window_get_title(_nativePtr));
        set { if (value is not null) RunOnMainThread(w => NativeMethods.wry_window_set_title(w._nativePtr, value)); }
    }

    /// <summary>Get the current URL, or set a URL to navigate to.</summary>
    public string? Url
    {
        get => NativeMethods.ReadAndFreeNativeString(NativeMethods.wry_window_get_url(_nativePtr));
        set { if (value is not null) RunOnMainThread(w => NativeMethods.wry_window_load_url(w._nativePtr, value)); }
    }

    /// <summary>Set HTML content to load.</summary>
    public string? Html
    {
        set { if (value is not null) RunOnMainThread(w => NativeMethods.wry_window_load_html(w._nativePtr, value)); }
    }

    /// <summary>Get or set window size in logical pixels.</summary>
    public (int Width, int Height) Size
    {
        get { NativeMethods.wry_window_get_size(_nativePtr, out var w, out var h); return (w, h); }
        set => RunOnMainThread(w => NativeMethods.wry_window_set_size(w._nativePtr, value.Width, value.Height));
    }

    /// <summary>Get or set window position in logical pixels.</summary>
    public (int X, int Y) Position
    {
        get { NativeMethods.wry_window_get_position(_nativePtr, out var x, out var y); return (x, y); }
        set => RunOnMainThread(w => NativeMethods.wry_window_set_position(w._nativePtr, value.X, value.Y));
    }

    /// <summary>Set minimum window inner size in logical pixels. Use (0, 0) to clear the constraint.</summary>
    public (int Width, int Height) MinSize
    {
        set => RunOnMainThread(w => NativeMethods.wry_window_set_min_size(w._nativePtr, value.Width, value.Height));
    }

    /// <summary>Set maximum window inner size in logical pixels. Use (0, 0) to clear the constraint.</summary>
    public (int Width, int Height) MaxSize
    {
        set => RunOnMainThread(w => NativeMethods.wry_window_set_max_size(w._nativePtr, value.Width, value.Height));
    }

    /// <summary>Get or set window theme. Auto = follow system; Dark/Light force a theme.</summary>
    public WryTheme Theme
    {
        get => (WryTheme)NativeMethods.wry_window_get_theme(_nativePtr);
        set => RunOnMainThread(w => NativeMethods.wry_window_set_theme(w._nativePtr, (int)value));
    }

    /// <summary>Get or set whether the window is resizable.</summary>
    public bool Resizable
    {
        get => NativeMethods.wry_window_get_resizable(_nativePtr);
        set => RunOnMainThread(w => NativeMethods.wry_window_set_resizable(w._nativePtr, value));
    }

    /// <summary>Get or set fullscreen state.</summary>
    public bool Fullscreen
    {
        get => NativeMethods.wry_window_get_fullscreen(_nativePtr);
        set => RunOnMainThread(w => NativeMethods.wry_window_set_fullscreen(w._nativePtr, value));
    }

    /// <summary>Get or set maximized state.</summary>
    public bool Maximized
    {
        get => NativeMethods.wry_window_get_maximized(_nativePtr);
        set => RunOnMainThread(w => NativeMethods.wry_window_set_maximized(w._nativePtr, value));
    }

    /// <summary>Get or set minimized state.</summary>
    public bool Minimized
    {
        get => NativeMethods.wry_window_get_minimized(_nativePtr);
        set => RunOnMainThread(w => NativeMethods.wry_window_set_minimized(w._nativePtr, value));
    }

    /// <summary>Set always-on-top state.</summary>
    public bool Topmost
    {
        set => RunOnMainThread(w => NativeMethods.wry_window_set_topmost(w._nativePtr, value));
    }

    /// <summary>Get or set window visibility.</summary>
    public bool Visible
    {
        get => NativeMethods.wry_window_get_visible(_nativePtr);
        set => RunOnMainThread(w => NativeMethods.wry_window_set_visible(w._nativePtr, value));
    }

    /// <summary>Get or set whether the window has title bar and borders.</summary>
    public bool Decorations
    {
        get => NativeMethods.wry_window_get_decorated(_nativePtr);
        set => RunOnMainThread(w => NativeMethods.wry_window_set_decorations(w._nativePtr, value));
    }

    /// <summary>Hide or show the window in the taskbar. Windows, Linux.</summary>
    public bool SkipTaskbar
    {
        set => RunOnMainThread(w => NativeMethods.wry_window_set_skip_taskbar(w._nativePtr, value));
    }

    /// <summary>Prevent window content from being captured (e.g. screen capture). Windows, macOS.</summary>
    public bool ContentProtected
    {
        set => RunOnMainThread(w => NativeMethods.wry_window_set_content_protected(w._nativePtr, value));
    }

    /// <summary>Show or hide drop shadow for undecorated windows. Windows.</summary>
    public bool Shadow
    {
        set => RunOnMainThread(w => NativeMethods.wry_window_set_shadow(w._nativePtr, value));
    }

    /// <summary>Keep the window below other windows.</summary>
    public bool AlwaysOnBottom
    {
        set => RunOnMainThread(w => NativeMethods.wry_window_set_always_on_bottom(w._nativePtr, value));
    }

    /// <summary>Allow or prevent maximizing the window.</summary>
    public bool Maximizable
    {
        set => RunOnMainThread(w => NativeMethods.wry_window_set_maximizable(w._nativePtr, value));
    }

    /// <summary>Allow or prevent minimizing the window.</summary>
    public bool Minimizable
    {
        set => RunOnMainThread(w => NativeMethods.wry_window_set_minimizable(w._nativePtr, value));
    }

    /// <summary>Allow or prevent closing the window (e.g. close button).</summary>
    public bool Closable
    {
        set => RunOnMainThread(w => NativeMethods.wry_window_set_closable(w._nativePtr, value));
    }

    /// <summary>Allow or prevent the window from receiving keyboard focus.</summary>
    public bool Focusable
    {
        set => RunOnMainThread(w => NativeMethods.wry_window_set_focusable(w._nativePtr, value));
    }

    /// <summary>Keep window within current monitor bounds when moved or resized.</summary>
    public bool PreventOverflow
    {
        get => _preventOverflow;
        set => _preventOverflow = value;
    }

    /// <summary>Set prevent-overflow margin in physical pixels (left, top, right, bottom). Use (0,0,0,0) for no margin.</summary>
    public void SetPreventOverflowMargin(int left, int top, int right, int bottom)
    {
        _preventOverflowMargin = (left, top, right, bottom);
    }

    /// <summary>Set the webview zoom level (1.0 = 100%).</summary>
    public double Zoom
    {
        set => RunOnMainThread(w => NativeMethods.wry_window_set_zoom(w._nativePtr, value));
    }

    /// <summary>Enable/disable devtools.</summary>
    public bool DevTools
    {
        set => RunOnMainThread(w =>
        {
            if (value) NativeMethods.wry_window_open_devtools(w._nativePtr);
            else NativeMethods.wry_window_close_devtools(w._nativePtr);
        });
    }

    /// <summary>Set background color. Ignored if Transparent is true.</summary>
    public WryColor BackgroundColor
    {
        set => RunOnMainThread(w => NativeMethods.wry_window_set_background_color(w._nativePtr, value.R, value.G, value.B, value.A));
    }

    /// <summary>
    /// Set the window icon from raw RGBA pixel data (4 bytes per pixel, row-major).
    /// Pass null or empty to clear. Windows and Linux only; macOS uses the .app bundle icon.
    /// </summary>
    public unsafe void SetIcon(byte[]? rgbaData, int width, int height)
    {
        RunOnMainThread(w =>
        {
            if (rgbaData is null || rgbaData.Length == 0)
            {
                NativeMethods.wry_window_set_icon(w._nativePtr, 0, 0, 0, 0);
                return;
            }
            fixed (byte* ptr = rgbaData)
            {
                NativeMethods.wry_window_set_icon(w._nativePtr, (nint)ptr, rgbaData.Length, width, height);
            }
        });
    }

    /// <summary>
    /// Set the window icon from encoded image file bytes (PNG, ICO, JPEG, BMP, GIF).
    /// Pass null or empty to clear. Windows and Linux only; macOS uses the .app bundle icon.
    /// </summary>
    public unsafe void SetIcon(byte[]? imageData)
    {
        RunOnMainThread(w =>
        {
            if (imageData is null || imageData.Length == 0)
            {
                NativeMethods.wry_window_set_icon_from_bytes(w._nativePtr, 0, 0);
                return;
            }
            fixed (byte* ptr = imageData)
            {
                NativeMethods.wry_window_set_icon_from_bytes(w._nativePtr, (nint)ptr, imageData.Length);
            }
        });
    }

    /// <summary>
    /// Set the window icon from an image file on disk (PNG, ICO, JPEG, BMP, GIF).
    /// Windows and Linux only; macOS uses the .app bundle icon.
    /// </summary>
    public void SetIconFromFile(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        var bytes = File.ReadAllBytes(filePath);
        SetIcon(bytes);
    }

    /// <summary>Center the window on the primary monitor.</summary>
    public void Center()
    {
        RunOnMainThread(w => NativeMethods.wry_window_center(w._nativePtr));
    }

    // =======================================================================
    // Post-run methods (call from events or dispatch)
    // =======================================================================

    /// <summary>Evaluate JavaScript in the webview (fire-and-forget).</summary>
    public void EvalJs(string js)
    {
        RunOnMainThread(w => NativeMethods.wry_window_eval_js(w._nativePtr, js));
    }

    /// <summary>
    /// Evaluate JavaScript in the webview and return the result.
    /// The result is the JSON-encoded value returned by the script.
    /// Must be called from the main thread (event callback or dispatch).
    /// </summary>
    public unsafe Task<string> EvalJsAsync(string js)
    {
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handle = GCHandle.Alloc(tcs);
        delegate* unmanaged[Cdecl]<nint, nint, void> fp = &EvalResultBridge;
        NativeMethods.wry_window_eval_js_callback(_nativePtr, js, (nint)fp, GCHandle.ToIntPtr(handle));
        return tcs.Task;
    }

    /// <summary>Navigate to a URL.</summary>
    public void LoadUrl(string url)
    {
        RunOnMainThread(w => NativeMethods.wry_window_load_url(w._nativePtr, url));
    }

    /// <summary>Load HTML content.</summary>
    public void LoadHtml(string html)
    {
        RunOnMainThread(w => NativeMethods.wry_window_load_html(w._nativePtr, html));
    }

    /// <summary>Open the print dialog.</summary>
    public void Print()
    {
        RunOnMainThread(w => NativeMethods.wry_window_print(w._nativePtr));
    }

    /// <summary>Reload the current page.</summary>
    public void Reload()
    {
        RunOnMainThread(w => NativeMethods.wry_window_reload(w._nativePtr));
    }

    /// <summary>Move focus to the webview.</summary>
    public void FocusWebView()
    {
        RunOnMainThread(w => NativeMethods.wry_window_focus(w._nativePtr));
    }

    /// <summary>Move focus from the webview back to the parent window.</summary>
    public void FocusParent()
    {
        RunOnMainThread(w => NativeMethods.wry_window_focus_parent(w._nativePtr));
    }

    /// <summary>Clear all browsing data.</summary>
    public void ClearAllBrowsingData()
    {
        RunOnMainThread(w => NativeMethods.wry_window_clear_all_browsing_data(w._nativePtr));
    }

    // =======================================================================
    // Cookies
    // =======================================================================

    /// <summary>
    /// Get all cookies that match the given URL.
    /// Must be called on the main thread (event callback or dispatch).
    /// </summary>
    public Cookie[] GetCookiesForUrl(string url)
    {
        ArgumentNullException.ThrowIfNull(url);
        var ptr = NativeMethods.wry_window_get_cookies_for_url(_nativePtr, url);
        return ParseCookieJson(ptr);
    }

    /// <summary>
    /// Get all cookies from the webview's cookie store.
    /// <para><b>Windows note:</b> this can deadlock when called from a synchronous event handler.
    /// Use from an async context or background thread.</para>
    /// </summary>
    public Cookie[] GetCookies()
    {
        var ptr = NativeMethods.wry_window_get_cookies(_nativePtr);
        return ParseCookieJson(ptr);
    }

    /// <summary>Add or update a cookie in the webview's cookie store.</summary>
    public void SetCookie(Cookie cookie)
    {
        ArgumentNullException.ThrowIfNull(cookie);
        double expires = cookie.Expires != DateTime.MinValue
            ? new DateTimeOffset(cookie.Expires.ToUniversalTime()).ToUnixTimeSeconds()
            : -1.0;
        RunOnMainThread(w => NativeMethods.wry_window_set_cookie(
            w._nativePtr,
            cookie.Name, cookie.Value,
            string.IsNullOrEmpty(cookie.Domain) ? null : cookie.Domain,
            string.IsNullOrEmpty(cookie.Path) ? null : cookie.Path,
            cookie.Secure, cookie.HttpOnly, expires));
    }

    /// <summary>Delete a cookie from the webview's cookie store.</summary>
    public void DeleteCookie(Cookie cookie)
    {
        ArgumentNullException.ThrowIfNull(cookie);
        RunOnMainThread(w => NativeMethods.wry_window_delete_cookie(
            w._nativePtr,
            cookie.Name, cookie.Value,
            string.IsNullOrEmpty(cookie.Domain) ? null : cookie.Domain,
            string.IsNullOrEmpty(cookie.Path) ? null : cookie.Path));
    }

    private static Cookie[] ParseCookieJson(nint ptr)
    {
        var json = NativeMethods.ReadAndFreeNativeString(ptr);
        if (string.IsNullOrEmpty(json))
            return [];

        using var doc = JsonDocument.Parse(json);
        var arr = doc.RootElement;
        var cookies = new Cookie[arr.GetArrayLength()];
        for (int i = 0; i < cookies.Length; i++)
        {
            var el = arr[i];
            var c = new Cookie(
                el.GetProperty("name").GetString() ?? "",
                el.GetProperty("value").GetString() ?? "");
            if (el.TryGetProperty("domain", out var d) && d.ValueKind == JsonValueKind.String)
                c.Domain = d.GetString() ?? "";
            if (el.TryGetProperty("path", out var p) && p.ValueKind == JsonValueKind.String)
                c.Path = p.GetString() ?? "";
            c.Secure = el.TryGetProperty("secure", out var s) && s.GetBoolean();
            c.HttpOnly = el.TryGetProperty("http_only", out var h) && h.GetBoolean();
            if (el.TryGetProperty("expires", out var exp) && exp.ValueKind == JsonValueKind.Number)
                c.Expires = DateTimeOffset.FromUnixTimeSeconds((long)exp.GetDouble()).DateTime;
            cookies[i] = c;
        }
        return cookies;
    }

    /// <summary>Request the window to close.</summary>
    public void Close()
    {
        RunOnMainThread(w => NativeMethods.wry_window_close(w._nativePtr));
    }

    /// <summary>Restore the window from minimized or maximized state.</summary>
    public void Restore()
    {
        RunOnMainThread(w => NativeMethods.wry_window_restore(w._nativePtr));
    }

    /// <summary>Whether the dev tools panel is currently open.</summary>
    public bool IsDevToolsOpen => NativeMethods.wry_window_is_devtools_open(_nativePtr);

    // =======================================================================
    // Post-run read-only properties
    // =======================================================================

    /// <summary>Get the DPI scale factor for the window's monitor.</summary>
    public double ScreenDpi
    {
        get
        {
            return NativeMethods.wry_window_get_screen_dpi(_nativePtr);
        }
    }

    /// <summary>Enumerate all available monitors.</summary>
    public unsafe List<MonitorInfo> GetAllMonitors()
    {
        var monitors = new List<MonitorInfo>();
        var handle = GCHandle.Alloc(monitors);
        try
        {
            delegate* unmanaged[Cdecl]<int, int, int, int, double, nint, void> fp = &MonitorBridge;
            NativeMethods.wry_window_get_all_monitors(_nativePtr, (nint)fp, GCHandle.ToIntPtr(handle));
        }
        finally
        {
            handle.Free();
        }
        return monitors;
    }

    // =======================================================================
    // Prevent overflow (clamp window to current monitor)
    // =======================================================================

    private MonitorInfo? FindCurrentMonitor(int pointX, int pointY)
    {
        var monitors = GetAllMonitors();
        foreach (var m in monitors)
        {
            if (pointX >= m.X && pointX < m.X + m.Width &&
                pointY >= m.Y && pointY < m.Y + m.Height)
                return m;
        }
        return monitors.Count > 0 ? monitors[0] : null;
    }

    private void ApplyPreventOverflow()
    {
        if (!_preventOverflow) return;
        NativeMethods.wry_window_get_size(_nativePtr, out var winW, out var winH);
        NativeMethods.wry_window_get_position(_nativePtr, out var winX, out var winY);
        var monitor = FindCurrentMonitor(winX + winW / 2, winY + winH / 2);
        if (monitor is not { } m) return;
        var (ml, mt, mr, mb) = _preventOverflowMargin;
        int left = m.X + ml;
        int top = m.Y + mt;
        int right = m.X + m.Width - mr;
        int bottom = m.Y + m.Height - mb;
        int maxX = Math.Max(right - winW, left);
        int maxY = Math.Max(bottom - winH, top);
        int newX = Math.Clamp(winX, left, maxX);
        int newY = Math.Clamp(winY, top, maxY);
        if (newX != winX || newY != winY)
            NativeMethods.wry_window_set_position(_nativePtr, newX, newY);
    }

    // =======================================================================
    // Cross-thread dispatch
    // =======================================================================

    private bool IsOnMainThread => Environment.CurrentManagedThreadId == _app.MainThreadId;

    /// <summary>
    /// Run an action on the event loop (main) thread. If already on the main thread,
    /// the action runs synchronously; otherwise it is dispatched asynchronously.
    /// </summary>
    private void RunOnMainThread(Action<WryWindow> action)
    {
        if (IsOnMainThread)
            action(this);
        else
            Dispatch(action);
    }

    /// <summary>
    /// Dispatch an action to run on the event loop (main) thread.
    /// Safe to call from any thread. The action receives this window
    /// and can call post-run methods on it.
    /// </summary>
    public unsafe void Dispatch(Action<WryWindow> action)
    {
        var captured = (Window: this, Action: action);
        var handle = GCHandle.Alloc(captured);
        delegate* unmanaged[Cdecl]<nint, nint, void> fp = &DispatchBridge;
        NativeMethods.wry_window_dispatch(_app.Handle, _windowId, (nint)fp, GCHandle.ToIntPtr(handle));
    }

    // =======================================================================
    // Internal: pointer capture
    // =======================================================================

    /// <summary>Queue a dispatch to capture the native WryWindow pointer after Init.</summary>
    internal unsafe void QueuePointerCapture()
    {
        delegate* unmanaged[Cdecl]<nint, nint, void> fp = &PointerCaptureBridge;
        NativeMethods.wry_window_dispatch(_app.Handle, _windowId, (nint)fp, GCHandlePtr);
    }

    /// <summary>Called when the window is materialized (by native window_created callback or pointer capture).</summary>
    internal void SetNativePtr(nint ptr)
    {
        _nativePtr = ptr;
    }

    /// <summary>Called by WryApp after Run() returns.</summary>
    internal void OnAppRunCompleted()
    {
        _nativePtr = 0;
    }

    internal void OnWindowDestroyed()
    {
        WindowDestroyed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Free the GCHandle. Called by WryApp.Dispose().</summary>
    internal void Cleanup()
    {
        _nativePtr = 0;
        if (_gcHandle.IsAllocated)
            _gcHandle.Free();
    }

    // =======================================================================
    // Static unmanaged callback bridges
    // =======================================================================

    private static WryWindow? Recover(nint ctx)
    {
        if (ctx == 0) return null;
        var handle = GCHandle.FromIntPtr(ctx);
        return handle.Target as WryWindow;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void PointerCaptureBridge(nint winPtr, nint ctx)
    {
        if (Recover(ctx) is { } win)
            win._nativePtr = winPtr;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void IpcBridge(nint message, nint url, nint ctx)
    {
        if (Recover(ctx) is { } win)
        {
            var msg = Marshal.PtrToStringUTF8(message) ?? "";
            var originUrl = Marshal.PtrToStringUTF8(url) ?? "";
            win.IpcMessageReceived?.Invoke(win, new IpcMessageEventArgs(msg, originUrl));
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static byte CloseBridge(nint ctx)
    {
        if (Recover(ctx) is { } win)
        {
            var args = new CloseRequestedEventArgs();
            win.CloseRequested?.Invoke(win, args);
            return (byte)(args.Cancel ? 0 : 1); // return 1 (true) to allow close
        }
        return 1; // allow close by default
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void ResizeBridge(int width, int height, nint ctx)
    {
        if (Recover(ctx) is { } win)
        {
            win.ApplyPreventOverflow();
            win.Resized?.Invoke(win, new SizeChangedEventArgs(width, height));
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void MoveBridge(int x, int y, nint ctx)
    {
        if (Recover(ctx) is { } win)
        {
            win.ApplyPreventOverflow();
            win.Moved?.Invoke(win, new PositionChangedEventArgs(x, y));
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void FocusBridge(byte focused, nint ctx)
    {
        if (Recover(ctx) is { } win)
            win.FocusChanged?.Invoke(win, new FocusChangedEventArgs(focused != 0));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void DispatchBridge(nint winPtr, nint ctx)
    {
        if (ctx == 0) return;
        var handle = GCHandle.FromIntPtr(ctx);
        try
        {
            if (handle.Target is (WryWindow window, Action<WryWindow> action))
            {
                // Ensure the native pointer is set (it should be from PointerCapture).
                if (window._nativePtr == 0)
                    window._nativePtr = winPtr;
                action.Invoke(window);
            }
        }
        finally
        {
            handle.Free();
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void MonitorBridge(int x, int y, int width, int height, double scale, nint ctx)
    {
        if (ctx == 0) return;
        var handle = GCHandle.FromIntPtr(ctx);
        if (handle.Target is List<MonitorInfo> list)
            list.Add(new MonitorInfo(x, y, width, height, scale));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static byte NavigationBridge(nint url, nint ctx)
    {
        if (Recover(ctx) is { } win)
        {
            var urlStr = Marshal.PtrToStringUTF8(url) ?? "";
            var args = new NavigatingEventArgs(urlStr);
            win.Navigating?.Invoke(win, args);
            return (byte)(args.Cancel ? 0 : 1); // 1 = allow, 0 = block
        }
        return 1; // allow by default
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void PageLoadBridge(int eventCode, nint url, nint ctx)
    {
        if (Recover(ctx) is { } win)
        {
            var urlStr = Marshal.PtrToStringUTF8(url) ?? "";
            var loadEvent = (WryPageLoadEvent)eventCode;
            win.PageLoad?.Invoke(win, new PageLoadEventArgs(loadEvent, urlStr));
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static byte DragDropBridge(int eventType, nint paths, int pathCount, int x, int y, nint ctx)
    {
        if (Recover(ctx) is { } win)
        {
            string[]? managedPaths = pathCount > 0 ? MarshalPathArray(paths, pathCount) : null;
            var args = new DragDropEventArgs((DragDropEventType)eventType, managedPaths, x, y);
            win.DragDrop?.Invoke(win, args);
            return (byte)(args.BlockDefault ? 1 : 0);
        }
        return 0; // don't block by default
    }

    /// <summary>Marshal an array of native UTF-8 string pointers into a managed string array.</summary>
    private static unsafe string[] MarshalPathArray(nint paths, int count)
    {
        var result = new string[count];
        var ptrs = (nint*)paths;
        for (int i = 0; i < count; i++)
        {
            result[i] = Marshal.PtrToStringUTF8(ptrs[i]) ?? "";
        }
        return result;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void EvalResultBridge(nint result, nint ctx)
    {
        if (ctx == 0) return;
        var handle = GCHandle.FromIntPtr(ctx);
        try
        {
            if (handle.Target is TaskCompletionSource<string> tcs)
            {
                var resultStr = Marshal.PtrToStringUTF8(result) ?? "";
                tcs.TrySetResult(resultStr);
            }
        }
        finally
        {
            handle.Free();
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void ProtocolBridge(nint url, nint method, nint headers, nint body, int bodyLen, nint ctx, nint responder)
    {
        if (ctx == 0) return;
        var handle = GCHandle.FromIntPtr(ctx);
        if (handle.Target is Func<ProtocolRequest, ProtocolResponse> handler)
        {
            var uri = Marshal.PtrToStringUTF8(url) ?? "";
            var httpMethod = Marshal.PtrToStringUTF8(method) ?? "GET";
            var headersStr = Marshal.PtrToStringUTF8(headers) ?? "";

            // Parse headers from "Key: Value\r\n" pairs
            var headerDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in headersStr.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
            {
                var sep = line.IndexOf(": ", StringComparison.Ordinal);
                if (sep > 0)
                    headerDict[line[..sep]] = line[(sep + 2)..];
            }

            // Copy body bytes
            byte[] bodyBytes;
            if (body != 0 && bodyLen > 0)
            {
                bodyBytes = new byte[bodyLen];
                unsafe { Marshal.Copy(body, bodyBytes, 0, bodyLen); }
            }
            else
            {
                bodyBytes = [];
            }

            var request = new ProtocolRequest(uri, httpMethod, headerDict, bodyBytes);
            ProtocolResponse response;
            try
            {
                response = handler(request);
            }
            catch
            {
                response = new ProtocolResponse
                {
                    StatusCode = 500,
                    ContentType = "text/plain",
                    Data = "Internal Error"u8.ToArray()
                };
            }

            // Serialize response headers to "Key: Value\r\n" format
            string? extraHeaders = null;
            if (response.Headers is { Count: > 0 })
            {
                var sb = new System.Text.StringBuilder();
                foreach (var (key, value) in response.Headers)
                {
                    sb.Append(key).Append(": ").Append(value).Append("\r\n");
                }
                extraHeaders = sb.ToString();
            }

            unsafe
            {
                fixed (byte* dataPtr = response.Data)
                {
                    NativeMethods.wry_protocol_respond(
                        responder,
                        (nint)dataPtr,
                        response.Data.Length,
                        response.ContentType,
                        response.StatusCode,
                        extraHeaders);
                }
            }
        }
    }
}
