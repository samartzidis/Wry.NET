using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

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
}

/// <summary>
/// Top-level application object. Owns the event loop and all windows.
/// Must be created and run on the main thread.
/// </summary>
/// <example>
/// <code>
/// using var app = new WryApp();
/// var window = app.CreateWindow();
/// window.Title = "Hello";
/// window.Url = "https://example.com";
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
    /// Raised when a window has been materialized and is live (initial or dynamic).
    /// Use this to run logic when a dynamically created window is ready.
    /// </summary>
    public event EventHandler<WindowCreatedEventArgs>? WindowCreated;

    /// <summary>
    /// Raised when dynamic window creation fails (async path). The window id and error message are provided.
    /// </summary>
    public event EventHandler<WindowCreationErrorEventArgs>? WindowCreationError;

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
    /// Create a new window. Configure it with properties before calling <see cref="Run"/>.
    /// </summary>
    public WryWindow CreateWindow()
    {
        return CreateWindow(owner: null);
    }

    /// <summary>
    /// Create a new window owned by <paramref name="owner"/> (e.g. so it stays on top of the owner and closes with it).
    /// Pass null for a top-level window.
    /// </summary>
    public WryWindow CreateWindow(WryWindow? owner)
    {
        return CreateWindow(owner, options: null);
    }

    /// <summary>
    /// Create a new window with optional owner and optional creation options.
    /// When <paramref name="options"/> is non-null, config (title, url, size, data directory) is passed at create time;
    /// otherwise the legacy path is used and you can configure via properties before <see cref="Run"/>.
    /// </summary>
    public unsafe WryWindow CreateWindow(WryWindow? owner, WryWindowCreateOptions? options)
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
            var ownerId = owner?.Id ?? 0u;
            var dataDir = options.DataDirectory ?? GetDefaultDataDirectory();
            nint titlePtr = 0, urlPtr = 0, htmlPtr = 0, dataDirPtr = 0;
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
                };
                WryWindow.PopulateCallbacks(ref config, window.GCHandlePtr);
                id = NativeMethods.wry_window_create(Handle, ownerId, 0, (nint)(&config));
            }
            finally
            {
                if (titlePtr != 0) Marshal.FreeCoTaskMem(titlePtr);
                if (urlPtr != 0) Marshal.FreeCoTaskMem(urlPtr);
                if (htmlPtr != 0) Marshal.FreeCoTaskMem(htmlPtr);
                if (dataDirPtr != 0) Marshal.FreeCoTaskMem(dataDirPtr);
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
        return window;
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
                window.OnWindowCreated();
                app.WindowCreated?.Invoke(app, new WindowCreatedEventArgs(window));
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
            app.WindowCreationError?.Invoke(app, new WindowCreationErrorEventArgs(windowId, message));
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
