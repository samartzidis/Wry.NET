using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Wry.NET;

/// <summary>
/// A native webview window. Configure properties before calling
/// <see cref="WryApp.Run"/>. Post-run operations (from events or dispatch)
/// are available after the event loop starts.
/// </summary>
public sealed class WryWindow
{
    private readonly WryApp _app;
    private readonly nuint _windowId;
    private nint _nativePtr; // set once the window is materialized in the event loop
    private GCHandle _gcHandle;
    private List<GCHandle>? _pinnedProtocolHandles; // keeps protocol handler delegates alive when set at create time

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

    internal WryWindow(WryApp app, nuint windowId)
    {
        _app = app;
        _windowId = windowId;
        _gcHandle = GCHandle.Alloc(this);
        // Data directory and other creation options are set via WryWindowCreateOptions when the window is created.
    }

    private nint GCHandlePtr => GCHandle.ToIntPtr(_gcHandle);

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
    /// Raised when this window has been materialized and is live (same moment as <see cref="WryApp.WindowCreated"/> for this window).
    /// </summary>
    public event EventHandler<EventArgs>? WindowCreated;

    /// <summary>
    /// Raised when this window has been destroyed (platform Destroyed event - e.g. user closed it or OS destroyed it with its owner).
    /// </summary>
    public event EventHandler<EventArgs>? WindowDestroyed;

    // =======================================================================
    // Properties (set before app.Run() to configure; getters available post-run)
    // =======================================================================

    /// <summary>Get or set the window title. Getter requires the window to be live.</summary>
    public string? Title
    {
        get
        {
            return NativeMethods.ReadAndFreeNativeString(NativeMethods.wry_window_get_title(_nativePtr));
        }
        set
        {
            if (value is null) return;
            NativeMethods.wry_window_set_title(_nativePtr, value);
        }
    }

    /// <summary>Get the current URL, or set a URL to navigate to.</summary>
    public string? Url
    {
        get
        {
            return NativeMethods.ReadAndFreeNativeString(NativeMethods.wry_window_get_url(_nativePtr));
        }
        set
        {
            if (value is null) return;
            NativeMethods.wry_window_load_url(_nativePtr, value);
        }
    }

    /// <summary>Set HTML content to load.</summary>
    public string? Html
    {
        set
        {
            if (value is null) return;
            NativeMethods.wry_window_load_html(_nativePtr, value);
        }
    }

    /// <summary>Get or set window size in logical pixels. Getter requires the window to be live.</summary>
    public (int Width, int Height) Size
    {
        get
        {
            NativeMethods.wry_window_get_size(_nativePtr, out var w, out var h);
            return (w, h);
        }
        set => NativeMethods.wry_window_set_size(_nativePtr, value.Width, value.Height);
    }

    /// <summary>Get or set window position in logical pixels. Getter requires the window to be live.</summary>
    public (int X, int Y) Position
    {
        get
        {
            NativeMethods.wry_window_get_position(_nativePtr, out var x, out var y);
            return (x, y);
        }
        set => NativeMethods.wry_window_set_position(_nativePtr, value.X, value.Y);
    }

    /// <summary>Set minimum window inner size in logical pixels. Use (0, 0) to clear the constraint.</summary>
    public (int Width, int Height) MinSize
    {
        set => NativeMethods.wry_window_set_min_size(_nativePtr, value.Width, value.Height);
    }

    /// <summary>Set maximum window inner size in logical pixels. Use (0, 0) to clear the constraint.</summary>
    public (int Width, int Height) MaxSize
    {
        set => NativeMethods.wry_window_set_max_size(_nativePtr, value.Width, value.Height);
    }

    /// <summary>Get or set window theme. Auto = follow system; Dark/Light force a theme. Behavior may be app-wide on Linux/macOS.</summary>
    public WryTheme Theme
    {
        get => (WryTheme)NativeMethods.wry_window_get_theme(_nativePtr);
        set => NativeMethods.wry_window_set_theme(_nativePtr, (int)value);
    }

    /// <summary>Get or set whether the window is resizable.</summary>
    public bool Resizable
    {
        get => NativeMethods.wry_window_get_resizable(_nativePtr);
        set => NativeMethods.wry_window_set_resizable(_nativePtr, value);
    }

    /// <summary>Get or set fullscreen state.</summary>
    public bool Fullscreen
    {
        get => NativeMethods.wry_window_get_fullscreen(_nativePtr);
        set => NativeMethods.wry_window_set_fullscreen(_nativePtr, value);
    }

    /// <summary>Get or set maximized state.</summary>
    public bool Maximized
    {
        get => NativeMethods.wry_window_get_maximized(_nativePtr);
        set => NativeMethods.wry_window_set_maximized(_nativePtr, value);
    }

    /// <summary>Get or set minimized state.</summary>
    public bool Minimized
    {
        get => NativeMethods.wry_window_get_minimized(_nativePtr);
        set => NativeMethods.wry_window_set_minimized(_nativePtr, value);
    }

    /// <summary>Set always-on-top state.</summary>
    public bool Topmost
    {
        set => NativeMethods.wry_window_set_topmost(_nativePtr, value);
    }

    /// <summary>Get or set window visibility.</summary>
    public bool Visible
    {
        get => NativeMethods.wry_window_get_visible(_nativePtr);
        set => NativeMethods.wry_window_set_visible(_nativePtr, value);
    }

    /// <summary>Get or set whether the window has title bar and borders.</summary>
    public bool Decorations
    {
        get => NativeMethods.wry_window_get_decorated(_nativePtr);
        set => NativeMethods.wry_window_set_decorations(_nativePtr, value);
    }

    /// <summary>Hide or show the window in the taskbar. Windows, Linux.</summary>
    public bool SkipTaskbar
    {
        set => NativeMethods.wry_window_set_skip_taskbar(_nativePtr, value);
    }

    /// <summary>Prevent window content from being captured (e.g. screen capture). Windows, macOS.</summary>
    public bool ContentProtected
    {
        set => NativeMethods.wry_window_set_content_protected(_nativePtr, value);
    }

    /// <summary>Show or hide drop shadow for undecorated windows. Windows.</summary>
    public bool Shadow
    {
        set => NativeMethods.wry_window_set_shadow(_nativePtr, value);
    }

    /// <summary>Keep the window below other windows.</summary>
    public bool AlwaysOnBottom
    {
        set => NativeMethods.wry_window_set_always_on_bottom(_nativePtr, value);
    }

    /// <summary>Allow or prevent maximizing the window.</summary>
    public bool Maximizable
    {
        set => NativeMethods.wry_window_set_maximizable(_nativePtr, value);
    }

    /// <summary>Allow or prevent minimizing the window.</summary>
    public bool Minimizable
    {
        set => NativeMethods.wry_window_set_minimizable(_nativePtr, value);
    }

    /// <summary>Allow or prevent closing the window (e.g. close button).</summary>
    public bool Closable
    {
        set => NativeMethods.wry_window_set_closable(_nativePtr, value);
    }

    /// <summary>Allow or prevent the window from receiving keyboard focus.</summary>
    public bool Focusable
    {
        set => NativeMethods.wry_window_set_focusable(_nativePtr, value);
    }

    /// <summary>Keep window within current monitor when moved or resized. Can be set before or after Run (use dispatch for post-run).</summary>
    public bool PreventOverflow
    {
        set => NativeMethods.wry_window_set_prevent_overflow(_nativePtr, value);
    }

    /// <summary>Set prevent_overflow margin in physical pixels (left, top, right, bottom). Use (0,0,0,0) for no margin.</summary>
    public void SetPreventOverflowMargin(int left, int top, int right, int bottom)
    {
        NativeMethods.wry_window_set_prevent_overflow_margin(_nativePtr, left, top, right, bottom);
    }

    /// <summary>Set the webview zoom level (1.0 = 100%).</summary>
    public double Zoom
    {
        set => NativeMethods.wry_window_set_zoom(_nativePtr, value);
    }

    /// <summary>Enable/disable devtools. Pre-run sets initial state; post-run opens/closes.</summary>
    public bool DevTools
    {
        set
        {
            if (value) NativeMethods.wry_window_open_devtools(_nativePtr);
            else NativeMethods.wry_window_close_devtools(_nativePtr);
        }
    }

    /// <summary>Set background color. Ignored if Transparent is true.</summary>
    public WryColor BackgroundColor
    {
        set => NativeMethods.wry_window_set_background_color(_nativePtr, value.R, value.G, value.B, value.A);
    }

    /// <summary>
    /// Set the window icon from raw RGBA pixel data (4 bytes per pixel, row-major).
    /// Pass null or empty to clear. Windows and Linux only; macOS uses the .app bundle icon.
    /// </summary>
    public unsafe void SetIcon(byte[]? rgbaData, int width, int height)
    {
        if (rgbaData is null || rgbaData.Length == 0)
        {
            NativeMethods.wry_window_set_icon(_nativePtr, 0, 0, 0, 0);
            return;
        }
        fixed (byte* ptr = rgbaData)
        {
            NativeMethods.wry_window_set_icon(_nativePtr, (nint)ptr, rgbaData.Length, width, height);
        }
    }

    /// <summary>
    /// Set the window icon from encoded image file bytes (PNG, ICO, JPEG, BMP, GIF).
    /// Pass null or empty to clear. Windows and Linux only; macOS uses the .app bundle icon.
    /// </summary>
    public unsafe void SetIcon(byte[]? imageData)
    {
        if (imageData is null || imageData.Length == 0)
        {
            NativeMethods.wry_window_set_icon_from_bytes(_nativePtr, 0, 0);
            return;
        }
        fixed (byte* ptr = imageData)
        {
            NativeMethods.wry_window_set_icon_from_bytes(_nativePtr, (nint)ptr, imageData.Length);
        }
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

    // =======================================================================
    // Pre-run methods
    // =======================================================================

    /// <summary>Add a JavaScript initialization script that runs before page load.</summary>
    public void AddInitScript(string js)
    {
        NativeMethods.wry_window_add_init_script(_app.Handle, _windowId, js);
    }

    /// <summary>
    /// Register a custom protocol handler. When the webview navigates to
    /// {scheme}://..., the handler is invoked with a <see cref="ProtocolRequest"/>
    /// containing the URL, method, headers, and body, and must return a
    /// <see cref="ProtocolResponse"/>.
    /// Must be called before <see cref="WryApp.Run"/>.
    /// </summary>
    public unsafe void AddCustomProtocol(string scheme, Func<ProtocolRequest, ProtocolResponse> handler)
    {
        // Pin the handler so it survives across native calls.
        var handle = GCHandle.Alloc(handler);
        var ctx = GCHandle.ToIntPtr(handle);

        delegate* unmanaged[Cdecl]<nint, nint, nint, nint, int, nint, nint, void> fp = &ProtocolBridge;
        NativeMethods.wry_window_add_custom_protocol(_app.Handle, _windowId, scheme, (nint)fp, ctx);

        // Note: the GCHandle is intentionally never freed — it must live for the
        // lifetime of the window. It will be collected when the process exits.
        // A more sophisticated implementation could track and free these.
    }

    /// <summary>
    /// Register a custom protocol handler (simplified overload). When the webview
    /// navigates to {scheme}://..., the handler is invoked with the URL string and
    /// must return a <see cref="ProtocolResponse"/>.
    /// Must be called before <see cref="WryApp.Run"/>.
    /// </summary>
    public void AddCustomProtocol(string scheme, Func<string, ProtocolResponse> handler)
    {
        AddCustomProtocol(scheme, (ProtocolRequest req) => handler(req.Url));
    }

    /// <summary>Center the window on the primary monitor.</summary>
    public void Center()
    {
        NativeMethods.wry_window_center(_nativePtr);
    }

    // =======================================================================
    // Post-run methods (call from events or dispatch)
    // =======================================================================

    /// <summary>Evaluate JavaScript in the webview (fire-and-forget).</summary>
    public void EvalJs(string js)
    {
        NativeMethods.wry_window_eval_js(_nativePtr, js);
    }

    /// <summary>
    /// Evaluate JavaScript in the webview and return the result.
    /// The result is the JSON-encoded value returned by the script.
    /// Must be called from a dispatch or event callback (post-run).
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
        NativeMethods.wry_window_load_url(_nativePtr, url);
    }

    /// <summary>Load HTML content.</summary>
    public void LoadHtml(string html)
    {
        NativeMethods.wry_window_load_html(_nativePtr, html);
    }

    /// <summary>Open the print dialog.</summary>
    public void Print()
    {
        NativeMethods.wry_window_print(_nativePtr);
    }

    /// <summary>Reload the current page.</summary>
    public void Reload()
    {
        NativeMethods.wry_window_reload(_nativePtr);
    }

    /// <summary>Move focus to the webview.</summary>
    public void FocusWebView()
    {
        NativeMethods.wry_window_focus(_nativePtr);
    }

    /// <summary>Move focus from the webview back to the parent window.</summary>
    public void FocusParent()
    {
        NativeMethods.wry_window_focus_parent(_nativePtr);
    }

    /// <summary>Clear all browsing data.</summary>
    public void ClearAllBrowsingData()
    {
        NativeMethods.wry_window_clear_all_browsing_data(_nativePtr);
    }

    /// <summary>Request the window to close.</summary>
    public void Close()
    {
        NativeMethods.wry_window_close(_nativePtr);
    }

    /// <summary>Restore the window from minimized or maximized state.</summary>
    public void Restore()
    {
        NativeMethods.wry_window_restore(_nativePtr);
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
    // Cross-thread dispatch
    // =======================================================================

    /// <summary>
    /// Dispatch an action to run on the event loop (main) thread.
    /// Safe to call from any thread. The action receives this window
    /// and can call post-run methods on it.
    /// </summary>
    public unsafe void Dispatch(Action<WryWindow> action)
    {
        // Capture this window in the closure so the bridge can invoke with it.
        var captured = (Window: this, Action: action);
        var handle = GCHandle.Alloc(captured);
        delegate* unmanaged[Cdecl]<nint, nint, void> fp = &DispatchBridge;
        NativeMethods.wry_window_dispatch(_app.Handle, _windowId, (nint)fp, GCHandle.ToIntPtr(handle));
    }

    // =======================================================================
    // Internal: callback registration & pointer capture
    // =======================================================================

    /// <summary>Register native event callbacks. Called by WryApp before Run().</summary>
    internal unsafe void RegisterNativeCallbacks()
    {
        var ctx = GCHandlePtr;

        if (IpcMessageReceived is not null || true) // always register so events can be attached later
        {
            delegate* unmanaged[Cdecl]<nint, nint, nint, void> ipcFp = &IpcBridge;
            NativeMethods.wry_window_set_ipc_handler(_app.Handle, _windowId, (nint)ipcFp, ctx);
        }

        {
            delegate* unmanaged[Cdecl]<nint, byte> closeFp = &CloseBridge;
            NativeMethods.wry_window_on_close(_app.Handle, _windowId, (nint)closeFp, ctx);
        }

        {
            delegate* unmanaged[Cdecl]<int, int, nint, void> resizeFp = &ResizeBridge;
            NativeMethods.wry_window_on_resize(_app.Handle, _windowId, (nint)resizeFp, ctx);
        }

        {
            delegate* unmanaged[Cdecl]<int, int, nint, void> moveFp = &MoveBridge;
            NativeMethods.wry_window_on_move(_app.Handle, _windowId, (nint)moveFp, ctx);
        }

        {
            delegate* unmanaged[Cdecl]<byte, nint, void> focusFp = &FocusBridge;
            NativeMethods.wry_window_on_focus(_app.Handle, _windowId, (nint)focusFp, ctx);
        }

        {
            delegate* unmanaged[Cdecl]<nint, nint, byte> navFp = &NavigationBridge;
            NativeMethods.wry_window_set_navigation_handler(_app.Handle, _windowId, (nint)navFp, ctx);
        }

        {
            delegate* unmanaged[Cdecl]<int, nint, nint, void> plFp = &PageLoadBridge;
            NativeMethods.wry_window_set_page_load_handler(_app.Handle, _windowId, (nint)plFp, ctx);
        }

        {
            delegate* unmanaged[Cdecl]<int, nint, int, int, int, nint, byte> ddFp = &DragDropBridge;
            NativeMethods.wry_window_on_drag_drop(_app.Handle, _windowId, (nint)ddFp, ctx);
        }
    }

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

    /// <summary>Raises WindowDestroyed. Called from the native bridge when the platform reports the window destroyed.</summary>
    internal void OnWindowCreated()
    {
        WindowCreated?.Invoke(this, EventArgs.Empty);
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
            win.Resized?.Invoke(win, new SizeChangedEventArgs(width, height));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void MoveBridge(int x, int y, nint ctx)
    {
        if (Recover(ctx) is { } win)
            win.Moved?.Invoke(win, new PositionChangedEventArgs(x, y));
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
