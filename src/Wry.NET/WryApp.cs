using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace Wry.NET;

/// <summary>
/// Options for creating a window with config in one call. All properties are optional; null/zero = use defaults.
/// Use with <see cref="WryApp.CreateWindow(WryWindow?, WryWindowCreateOptions?)"/> to avoid separate setters before run.
/// </summary>
public sealed class WryWindowCreateOptions
{
    /// <summary>Window title. Default is empty.</summary>
    public string? Title { get; set; }

    /// <summary>Initial URL to load. Ignored if <see cref="Html"/> is set.</summary>
    public string? Url { get; set; }

    /// <summary>Initial HTML content. If set, <see cref="Url"/> is ignored.</summary>
    public string? Html { get; set; }

    /// <summary>Initial width in pixels. Use 0 or leave unset for default (800).</summary>
    public int Width { get; set; }

    /// <summary>Initial height in pixels. Use 0 or leave unset for default (600).</summary>
    public int Height { get; set; }

    /// <summary>WebView user data directory (e.g. for WebView2). Default is %LOCALAPPDATA%/[AppName] when null.</summary>
    public string? DataDirectory { get; set; }

    /// <summary>
    /// Custom protocol handlers (scheme + handler) to register at create time. Use for embedded/disk asset servers
    /// so that both main and dynamic windows get the protocol when they are created.
    /// </summary>
    public List<(string Scheme, Func<ProtocolRequest, ProtocolResponse> Handler)>? Protocols { get; set; }

    /// <summary>Enable default context menus (e.g. right-click). Default true. Windows only.</summary>
    public bool DefaultContextMenus { get; set; } = true;

    /// <summary>Path to window icon image file (PNG, ICO, JPEG, BMP, GIF). Windows and Linux only; macOS uses .app bundle icon.</summary>
    public string? IconPath { get; set; }

    /// <summary>JavaScript init scripts injected before page load. Add scripts here instead of calling AddInitScript after creation.</summary>
    public List<string>? InitScripts { get; set; }

    /// <summary>Minimum window size in pixels (width, height). Null = no minimum.</summary>
    public (int Width, int Height)? MinSize { get; set; }

    /// <summary>Maximum window size in pixels (width, height). Null = no maximum.</summary>
    public (int Width, int Height)? MaxSize { get; set; }

    /// <summary>Initial window position in pixels (x, y). Null = OS default.</summary>
    public (int X, int Y)? Position { get; set; }

    /// <summary>Whether the window is resizable. Default true.</summary>
    public bool Resizable { get; set; } = true;

    /// <summary>Whether the window starts in fullscreen mode. Default false.</summary>
    public bool Fullscreen { get; set; }

    /// <summary>Whether the window starts maximized. Default false.</summary>
    public bool Maximized { get; set; }

    /// <summary>Whether the window starts minimized. Default false.</summary>
    public bool Minimized { get; set; }

    /// <summary>Whether the window is always on top. Default false.</summary>
    public bool Topmost { get; set; }

    /// <summary>Whether the window is visible. Default true.</summary>
    public bool Visible { get; set; } = true;

    /// <summary>Enable developer tools. Default false.</summary>
    public bool Devtools { get; set; }

    /// <summary>Whether the window is transparent. Default false.</summary>
    public bool Transparent { get; set; }

    /// <summary>Whether the window has decorations (title bar, borders). Default true.</summary>
    public bool Decorations { get; set; } = true;

    /// <summary>Custom user agent string. Null = browser default.</summary>
    public string? UserAgent { get; set; }

    /// <summary>Initial zoom level. Default 1.0. Values &lt;= 0 are ignored.</summary>
    public double Zoom { get; set; } = 1.0;

    /// <summary>Enable back/forward navigation gestures. Default false. macOS only.</summary>
    public bool BackForwardGestures { get; set; }

    /// <summary>Enable media autoplay. Default false.</summary>
    public bool Autoplay { get; set; }

    /// <summary>Enable hotkey-based zoom (Ctrl+/Ctrl-). Default true.</summary>
    public bool HotkeysZoom { get; set; } = true;

    /// <summary>Enable clipboard access from JavaScript. Default false.</summary>
    public bool Clipboard { get; set; }

    /// <summary>Accept first mouse click when window is not focused. Default false. macOS only.</summary>
    public bool AcceptFirstMouse { get; set; }

    /// <summary>Use incognito/private browsing mode. Default false.</summary>
    public bool Incognito { get; set; }

    /// <summary>Whether the window receives focus when created. Default true.</summary>
    public bool Focused { get; set; } = true;

    /// <summary>Disable JavaScript execution. Default false.</summary>
    public bool JavascriptDisabled { get; set; }

    /// <summary>Background color as (R, G, B, A). Null = platform default.</summary>
    public (byte R, byte G, byte B, byte A)? BackgroundColor { get; set; }

    /// <summary>Background throttling policy. Null = default. Windows only.</summary>
    public int? BackgroundThrottling { get; set; }

    /// <summary>Window theme. 0 = system, 1 = light, 2 = dark. Default 0. Windows only.</summary>
    public int Theme { get; set; }

    /// <summary>Use HTTPS scheme for custom protocols. Default false. Windows only.</summary>
    public bool HttpsScheme { get; set; }

    /// <summary>Enable browser accelerator keys (F5, Ctrl+R, etc.). Default true. Windows only.</summary>
    public bool BrowserAcceleratorKeys { get; set; } = true;

    /// <summary>Scrollbar style. 0 = default, 1 = fluent overlay, 2 = none. Windows only.</summary>
    public int ScrollBarStyle { get; set; }

    /// <summary>Hide the window from the taskbar. Default false.</summary>
    public bool SkipTaskbar { get; set; }

    /// <summary>Prevent screen capture of the window content. Default false.</summary>
    public bool ContentProtected { get; set; }

    /// <summary>Enable window shadow. Default true.</summary>
    public bool Shadow { get; set; } = true;

    /// <summary>Keep the window below other windows. Default false.</summary>
    public bool AlwaysOnBottom { get; set; }

    /// <summary>Allow the window to be maximized. Default true.</summary>
    public bool Maximizable { get; set; } = true;

    /// <summary>Allow the window to be minimized. Default true.</summary>
    public bool Minimizable { get; set; } = true;

    /// <summary>Allow the window to be closed. Default true.</summary>
    public bool Closable { get; set; } = true;

    /// <summary>Allow the window to receive focus. Default true.</summary>
    public bool Focusable { get; set; } = true;

    /// <summary>Custom window class name. Null = default. Windows only.</summary>
    public string? WindowClassname { get; set; }

    /// <summary>
    /// Hooks invoked with the live window when it is materialized, before the user's onCreated callback.
    /// Extensions use this to auto-attach behavior at creation time.
    /// </summary>
    public List<Action<WryWindow>>? WindowCreatedActions { get; set; }
}

/// <summary>
/// Top-level application object. Owns the event loop and all windows.
/// Must be created and run on the main thread.
/// </summary>
/// <example>
/// <code>
/// using var app = new WryApp();
/// app.CreateWindow(new WryWindowCreateOptions
/// {
///     Title = "Hello",
///     Url = "https://example.com",
/// });
/// app.Run(); // blocks until all windows close
/// </code>
/// </example>
public sealed class WryApp : IDisposable
{
    internal nint Handle { get; private set; }
    private bool _disposed;
    private readonly List<WryWindow> _windows = [];
    private readonly List<WryTrayIcon> _trays = [];
    private GCHandle _gcHandle;
    private readonly Dictionary<nuint, Action<WryWindow>?> _onCreatedCallbacks = [];
    private readonly Dictionary<nuint, Action<string>?> _onErrorCallbacks = [];
    internal int MainThreadId { get; } = Environment.CurrentManagedThreadId;

    /// <summary>All windows created by this app.</summary>
    public IReadOnlyList<WryWindow> Windows => _windows;

    /// <summary>All tray icons created by this app.</summary>
    public IReadOnlyList<WryTrayIcon> TrayIcons => _trays;

    /// <summary>
    /// Raised when all windows have closed. Set
    /// <see cref="ExitRequestedEventArgs.Cancel"/> to true to keep the event
    /// loop running (e.g. for tray-icon-only mode). If no handler cancels,
    /// the application exits and any remaining tray icons are removed
    /// automatically.
    /// </summary>
    public event EventHandler<ExitRequestedEventArgs>? ExitRequested;

    /// <summary>
    /// Raised when any window has been destroyed (platform Destroyed event).
    /// For per-window subscription, use <see cref="WryWindow.WindowDestroyed"/> instead.
    /// </summary>
    public event EventHandler<WindowDestroyedEventArgs>? WindowDestroyed;

    public WryApp()
    {
        Handle = NativeMethods.wry_app_new();
        if (Handle == 0)
            throw new InvalidOperationException("Failed to create WryApp native handle.");
        _gcHandle = GCHandle.Alloc(this);
    }

    /// <summary>
    /// Create a new window. The window is materialized asynchronously; use <paramref name="onCreated"/>
    /// to receive the live <see cref="WryWindow"/> when it is ready.
    /// </summary>
    /// <param name="options">Creation options. Null uses defaults.</param>
    /// <param name="onCreated">Called with the live window when materialization succeeds.</param>
    /// <param name="onError">Called with an error message if creation fails.</param>
    public void CreateWindow(
        WryWindowCreateOptions? options = null,
        Action<WryWindow>? onCreated = null,
        Action<string>? onError = null)
    {
        CreateWindow(owner: null, options, onCreated, onError);
    }

    /// <summary>
    /// Create a new window owned by <paramref name="owner"/>. The window is materialized asynchronously;
    /// use <paramref name="onCreated"/> to receive the live <see cref="WryWindow"/> when it is ready.
    /// </summary>
    /// <param name="owner">Owner window (stays on top of owner, closes with it). Null for top-level.</param>
    /// <param name="options">Creation options. Null uses defaults.</param>
    /// <param name="onCreated">Called with the live window when materialization succeeds.</param>
    /// <param name="onError">Called with an error message if creation fails.</param>
    public unsafe void CreateWindow(
        WryWindow? owner,
        WryWindowCreateOptions? options = null,
        Action<WryWindow>? onCreated = null,
        Action<string>? onError = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (options is null)
        {
            options = new WryWindowCreateOptions { DataDirectory = GetDefaultDataDirectory() };
        }

        var window = new WryWindow(this);
        _windows.Add(window);

        List<GCHandle>? pinnedProtocolHandles = null;
        byte[]? iconBytes = null;
        GCHandle iconHandle = default;
        nuint id;
        {
            var dataDir = options.DataDirectory ?? GetDefaultDataDirectory();
            nint titlePtr = 0, urlPtr = 0, htmlPtr = 0, dataDirPtr = 0;
            nint userAgentPtr = 0, windowClassnamePtr = 0;
            nint protocolsPtr = 0;
            int protocolCount = 0;
            var schemePtrsToFree = new List<nint>();
            nint initScriptsArrayPtr = 0;
            var initScriptPtrs = new List<nint>();

            if (!string.IsNullOrEmpty(options.IconPath) && File.Exists(options.IconPath))
            {
                iconBytes = File.ReadAllBytes(options.IconPath);
                if (iconBytes.Length > 0)
                    iconHandle = GCHandle.Alloc(iconBytes, GCHandleType.Pinned);
            }

            try
            {
                if (!string.IsNullOrEmpty(options.Title)) titlePtr = Marshal.StringToCoTaskMemUTF8(options.Title);
                if (!string.IsNullOrEmpty(options.Url)) urlPtr = Marshal.StringToCoTaskMemUTF8(options.Url);
                if (!string.IsNullOrEmpty(options.Html)) htmlPtr = Marshal.StringToCoTaskMemUTF8(options.Html);
                if (!string.IsNullOrEmpty(dataDir)) dataDirPtr = Marshal.StringToCoTaskMemUTF8(dataDir);
                if (!string.IsNullOrEmpty(options.UserAgent)) userAgentPtr = Marshal.StringToCoTaskMemUTF8(options.UserAgent);
                if (!string.IsNullOrEmpty(options.WindowClassname)) windowClassnamePtr = Marshal.StringToCoTaskMemUTF8(options.WindowClassname);

                if (options.Protocols is { Count: > 0 } protocols)
                {
                    pinnedProtocolHandles = [];
                    var entries = new List<NativeMethods.WryProtocolEntryNative>();
                    foreach (var (scheme, handler) in protocols)
                    {
                        if (string.IsNullOrEmpty(scheme)) continue;
                        var h = GCHandle.Alloc(handler);
                        pinnedProtocolHandles.Add(h);
                        var schemePtr = Marshal.StringToCoTaskMemUTF8(scheme);
                        schemePtrsToFree.Add(schemePtr);
                        entries.Add(new NativeMethods.WryProtocolEntryNative
                        {
                            Scheme = schemePtr,
                            Callback = WryWindow.GetProtocolBridgePointer(),
                            Context = GCHandle.ToIntPtr(h),
                        });
                    }
                    protocolCount = entries.Count;
                    if (protocolCount > 0)
                    {
                        var stride = Marshal.SizeOf<NativeMethods.WryProtocolEntryNative>();
                        protocolsPtr = Marshal.AllocHGlobal(protocolCount * stride);
                        for (var i = 0; i < protocolCount; i++)
                            Marshal.StructureToPtr(entries[i], protocolsPtr + i * stride, false);
                    }
                }

                int initScriptCount = 0;
                if (options.InitScripts is { Count: > 0 } scripts)
                {
                    foreach (var script in scripts)
                    {
                        if (string.IsNullOrEmpty(script)) continue;
                        initScriptPtrs.Add(Marshal.StringToCoTaskMemUTF8(script));
                    }
                    initScriptCount = initScriptPtrs.Count;
                    if (initScriptCount > 0)
                    {
                        initScriptsArrayPtr = Marshal.AllocHGlobal(initScriptCount * nint.Size);
                        for (var i = 0; i < initScriptCount; i++)
                            Marshal.WriteIntPtr(initScriptsArrayPtr, i * nint.Size, initScriptPtrs[i]);
                    }
                }

                var config = new NativeMethods.WryWindowConfigNative
                {
                    Title = titlePtr,
                    Url = urlPtr,
                    Html = htmlPtr,
                    Width = options.Width > 0 ? options.Width : 0,
                    Height = options.Height > 0 ? options.Height : 0,
                    DataDirectory = dataDirPtr,
                    ProtocolCount = protocolCount,
                    Protocols = protocolsPtr,
                    DefaultContextMenus = options.DefaultContextMenus ? 1 : 0,
                    IconData = iconHandle.IsAllocated ? iconHandle.AddrOfPinnedObject() : 0,
                    IconDataLen = iconBytes?.Length ?? 0,
                    InitScriptCount = initScriptCount,
                    InitScripts = initScriptsArrayPtr,
                    MinWidth = options.MinSize?.Width ?? 0,
                    MinHeight = options.MinSize?.Height ?? 0,
                    MaxWidth = options.MaxSize?.Width ?? 0,
                    MaxHeight = options.MaxSize?.Height ?? 0,
                    HasPosition = options.Position.HasValue ? 1 : 0,
                    X = options.Position?.X ?? 0,
                    Y = options.Position?.Y ?? 0,
                    Resizable = options.Resizable ? 1 : 0,
                    Fullscreen = options.Fullscreen ? 1 : 0,
                    Maximized = options.Maximized ? 1 : 0,
                    Minimized = options.Minimized ? 1 : 0,
                    Topmost = options.Topmost ? 1 : 0,
                    Visible = options.Visible ? 1 : 0,
                    Devtools = options.Devtools ? 1 : 0,
                    Transparent = options.Transparent ? 1 : 0,
                    Decorations = options.Decorations ? 1 : 0,
                    UserAgent = userAgentPtr,
                    Zoom = options.Zoom,
                    BackForwardGestures = options.BackForwardGestures ? 1 : 0,
                    Autoplay = options.Autoplay ? 1 : 0,
                    HotkeysZoom = options.HotkeysZoom ? 1 : 0,
                    Clipboard = options.Clipboard ? 1 : 0,
                    AcceptFirstMouse = options.AcceptFirstMouse ? 1 : 0,
                    Incognito = options.Incognito ? 1 : 0,
                    Focused = options.Focused ? 1 : 0,
                    JavascriptDisabled = options.JavascriptDisabled ? 1 : 0,
                    HasBackgroundColor = options.BackgroundColor.HasValue ? 1 : 0,
                    BgR = options.BackgroundColor?.R ?? 0,
                    BgG = options.BackgroundColor?.G ?? 0,
                    BgB = options.BackgroundColor?.B ?? 0,
                    BgA = options.BackgroundColor?.A ?? 0,
                    HasBackgroundThrottling = options.BackgroundThrottling.HasValue ? 1 : 0,
                    BackgroundThrottling = options.BackgroundThrottling ?? 0,
                    Theme = options.Theme,
                    HttpsScheme = options.HttpsScheme ? 1 : 0,
                    BrowserAcceleratorKeys = options.BrowserAcceleratorKeys ? 1 : 0,
                    ScrollBarStyle = options.ScrollBarStyle,
                    SkipTaskbar = options.SkipTaskbar ? 1 : 0,
                    ContentProtected = options.ContentProtected ? 1 : 0,
                    Shadow = options.Shadow ? 1 : 0,
                    AlwaysOnBottom = options.AlwaysOnBottom ? 1 : 0,
                    Maximizable = options.Maximizable ? 1 : 0,
                    Minimizable = options.Minimizable ? 1 : 0,
                    Closable = options.Closable ? 1 : 0,
                    Focusable = options.Focusable ? 1 : 0,
                    WindowClassname = windowClassnamePtr,
                    OwnerWindowId = owner?.Id ?? 0u,
                    ParentWindowId = 0,
                };
                WryWindow.PopulateCallbacks(ref config, window.GCHandlePtr);
                id = NativeMethods.wry_window_create(Handle, 0, 0, (nint)(&config));
            }
            finally
            {
                if (titlePtr != 0) Marshal.FreeCoTaskMem(titlePtr);
                if (urlPtr != 0) Marshal.FreeCoTaskMem(urlPtr);
                if (htmlPtr != 0) Marshal.FreeCoTaskMem(htmlPtr);
                if (dataDirPtr != 0) Marshal.FreeCoTaskMem(dataDirPtr);
                if (userAgentPtr != 0) Marshal.FreeCoTaskMem(userAgentPtr);
                if (windowClassnamePtr != 0) Marshal.FreeCoTaskMem(windowClassnamePtr);
                foreach (var p in schemePtrsToFree)
                    if (p != 0) Marshal.FreeCoTaskMem(p);
                if (protocolsPtr != 0) Marshal.FreeHGlobal(protocolsPtr);
                if (iconHandle.IsAllocated) iconHandle.Free();
                foreach (var p in initScriptPtrs)
                    if (p != 0) Marshal.FreeCoTaskMem(p);
                if (initScriptsArrayPtr != 0) Marshal.FreeHGlobal(initScriptsArrayPtr);
            }
        }

        if (id == 0)
            throw new InvalidOperationException("Failed to create native window.");

        window.SetWindowId(id);
        if (pinnedProtocolHandles != null)
            window.AddPinnedProtocolHandles(pinnedProtocolHandles);

        if (options.WindowCreatedActions is { Count: > 0 } hooks)
        {
            var userCallback = onCreated;
            var capturedHooks = hooks.ToArray();
            onCreated = w =>
            {
                foreach (var hook in capturedHooks)
                    hook(w);
                userCallback?.Invoke(w);
            };
        }

        _onCreatedCallbacks[id] = onCreated;
        _onErrorCallbacks[id] = onError;
    }

    private static string GetDefaultDataDirectory()
    {
        var appName = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name ?? "WryApp";
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            appName);
    }

    /// <summary>
    /// Create a new tray icon. Configure it with properties before calling <see cref="Run"/>.
    /// </summary>
    public WryTrayIcon CreateTrayIcon()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var id = NativeMethods.wry_tray_new(Handle);
        if (id == 0)
            throw new InvalidOperationException("Failed to create native tray icon.");

        var tray = new WryTrayIcon(this, id);
        _trays.Add(tray);
        return tray;
    }

    /// <summary>
    /// On Windows, the thread must be STA for COM/OLE (required by the native layer).
    /// Throws a clear exception before entering the native event loop so the user gets a helpful message.
    /// </summary>
    private static void EnsureStaThreadForRun()
    {
        if (!OperatingSystem.IsWindows())
            return;
        var apt = Thread.CurrentThread.GetApartmentState();
        if (apt == ApartmentState.STA)
            return;
        throw new InvalidOperationException(
            "On Windows, the thread that calls WryApp.Run() must be STA (single-threaded apartment). " +
            "Add [STAThread] to your Main method, e.g.: [STAThread] static void Main(string[] args) { ... }.");
    }

    /// <summary>
    /// Run the application event loop. Blocks the calling thread until the
    /// application exits. Must be called on the main thread.
    /// <para>
    /// By default, the application exits when all windows close. Any remaining
    /// tray icons are removed automatically. To keep the event loop running
    /// after all windows close (e.g. for tray-icon-only mode), handle the
    /// <see cref="ExitRequested"/> event and set Cancel to true.
    /// </para>
    /// </summary>
    public unsafe void Run()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Register native callbacks for tray icons (window callbacks are passed at create time).
        foreach (var tray in _trays)
            tray.RegisterNativeCallbacks();

        // Register the exit-requested callback.
        delegate* unmanaged[Cdecl]<byte, int, nint, byte> fp = &ExitRequestedBridge;
        NativeMethods.wry_app_on_exit_requested(Handle, (nint)fp, GCHandle.ToIntPtr(_gcHandle));

        // Register window-created and creation-error callbacks (for initial and dynamic windows).
        delegate* unmanaged[Cdecl]<nint, nuint, nint, void> onCreated = &WindowCreatedBridge;
        NativeMethods.wry_app_on_window_created(Handle, (nint)onCreated, GCHandle.ToIntPtr(_gcHandle));
        delegate* unmanaged[Cdecl]<nint, nuint, nint, void> onError = &WindowCreationErrorBridge;
        NativeMethods.wry_app_on_window_creation_error(Handle, (nint)onError, GCHandle.ToIntPtr(_gcHandle));
        delegate* unmanaged[Cdecl]<nint, nuint, void> onDestroyed = &WindowDestroyedBridge;
        NativeMethods.wry_app_on_window_destroyed(Handle, (nint)onDestroyed, GCHandle.ToIntPtr(_gcHandle));

        // Queue dispatches to capture native pointers after Init (trays only; windows use window_created callback).
        foreach (var tray in _trays)
            tray.QueuePointerCapture();

        EnsureStaThreadForRun();

        // This blocks until the application exits.
        NativeMethods.wry_app_run(Handle);

        // After run returns, clear native pointers.
        foreach (var window in _windows)
            window.OnAppRunCompleted();
        foreach (var tray in _trays)
            tray.OnAppRunCompleted();
    }

    /// <summary>
    /// Request the application to exit with the given exit code. This fires
    /// the <see cref="ExitRequested"/> event with <see cref="ExitRequestedEventArgs.ExitCode"/>
    /// set to <paramref name="exitCode"/>. If no handler cancels, the event
    /// loop exits and any remaining tray icons are removed. Safe to call from
    /// any thread.
    /// </summary>
    /// <param name="exitCode">The exit code (default 0).</param>
    public void Exit(int exitCode = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        NativeMethods.wry_app_exit(Handle, exitCode);
    }

    /// <summary>
    /// Get the WebView engine version string (e.g. Chromium/WebKit version).
    /// </summary>
    public static string? GetWebViewVersion()
    {
        return NativeMethods.ReadAndFreeNativeString(NativeMethods.wry_webview_version());
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var window in _windows)
            window.Cleanup();
        _windows.Clear();

        foreach (var tray in _trays)
            tray.Cleanup();
        _trays.Clear();

        if (_gcHandle.IsAllocated)
            _gcHandle.Free();

        if (Handle != 0)
        {
            NativeMethods.wry_app_destroy(Handle);
            Handle = 0;
        }
    }

    // =======================================================================
    // Static unmanaged callback bridge
    // =======================================================================

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static byte ExitRequestedBridge(byte hasCode, int code, nint ctx)
    {
        if (ctx == 0) return 1; // allow exit by default
        var handle = GCHandle.FromIntPtr(ctx);
        if (handle.Target is WryApp app)
        {
            int? exitCode = hasCode != 0 ? code : null;
            var args = new ExitRequestedEventArgs(exitCode);
            app.ExitRequested?.Invoke(app, args);
            return (byte)(args.Cancel ? 0 : 1); // 1 = allow exit, 0 = prevent
        }
        return 1; // allow exit by default
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void WindowCreatedBridge(nint ctx, nuint windowId, nint windowPtr)
    {
        if (ctx == 0 || windowPtr == 0) return;
        var handle = GCHandle.FromIntPtr(ctx);
        if (handle.Target is WryApp app)
        {
            WryWindow? window = null;
            foreach (var w in app.Windows)
            {
                if (w.Id == windowId) { window = w; break; }
            }
            if (window is not null)
            {
                window.SetNativePtr(windowPtr);

                if (app._onCreatedCallbacks.Remove(windowId, out var cb) && cb is not null)
                    cb(window);
                app._onErrorCallbacks.Remove(windowId);
            }
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void WindowCreationErrorBridge(nint ctx, nuint windowId, nint errorMessagePtr)
    {
        if (ctx == 0) return;
        var handle = GCHandle.FromIntPtr(ctx);
        if (handle.Target is WryApp app)
        {
            var message = errorMessagePtr != 0 ? Marshal.PtrToStringUTF8(errorMessagePtr) ?? "" : "";

            if (app._onErrorCallbacks.Remove(windowId, out var cb) && cb is not null)
                cb(message);
            app._onCreatedCallbacks.Remove(windowId);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void WindowDestroyedBridge(nint ctx, nuint windowId)
    {
        if (ctx == 0) return;
        var handle = GCHandle.FromIntPtr(ctx);
        if (handle.Target is not WryApp app) return;

        WryWindow? window = null;
        foreach (var w in app.Windows)
        {
            if (w.Id == windowId) { window = w; break; }
        }
        window?.OnWindowDestroyed();
        app.WindowDestroyed?.Invoke(app, new WindowDestroyedEventArgs(windowId, window));
    }
}
