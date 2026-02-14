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

    /// <summary>All windows created by this app.</summary>
    public IReadOnlyList<WryWindow> Windows => _windows;

    public WryApp()
    {
        Handle = NativeMethods.wry_app_new();
        if (Handle == 0)
            throw new InvalidOperationException("Failed to create WryApp native handle.");
    }

    /// <summary>
    /// Create a new window. Configure it with properties before calling <see cref="Run"/>.
    /// </summary>
    public WryWindow CreateWindow()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var id = NativeMethods.wry_window_new(Handle);
        if (id == 0)
            throw new InvalidOperationException("Failed to create native window.");

        var window = new WryWindow(this, id);
        _windows.Add(window);
        return window;
    }

    /// <summary>
    /// Run the application event loop. Blocks the calling thread until all
    /// windows are closed. Must be called on the main thread.
    /// </summary>
    public void Run()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Register native callbacks for all windows before entering the event loop.
        foreach (var window in _windows)
        {
            window.RegisterNativeCallbacks();
        }

        // Queue a dispatch for each window to capture its native pointer.
        // These fire right after Init (window creation) in the event loop.
        foreach (var window in _windows)
        {
            window.QueuePointerCapture();
        }

        // This blocks until all windows are closed.
        NativeMethods.wry_app_run(Handle);

        // After run returns, clear native pointers (windows are destroyed).
        foreach (var window in _windows)
        {
            window.OnAppRunCompleted();
        }
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
        {
            window.Cleanup();
        }
        _windows.Clear();

        if (Handle != 0)
        {
            NativeMethods.wry_app_destroy(Handle);
            Handle = 0;
        }
    }
}
