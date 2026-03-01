using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Wry.NET;

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
        ObjectDisposedException.ThrowIf(_disposed, this);

        var id = owner is null
            ? NativeMethods.wry_window_new(Handle)
            : NativeMethods.wry_window_new_with_owner(Handle, owner.Id);
        if (id == 0)
            throw new InvalidOperationException("Failed to create native window.");

        var window = new WryWindow(this, id);
        _windows.Add(window);
        window.RegisterNativeCallbacks();
        return window;
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

        // Register native callbacks for all windows and tray icons.
        foreach (var window in _windows)
            window.RegisterNativeCallbacks();
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
