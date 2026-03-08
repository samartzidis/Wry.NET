using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Wry.NET;

/// <summary>
/// Options for creating a tray icon with all configuration in one call.
/// Use with <see cref="WryApp.CreateTrayIcon(WryTrayIconCreateOptions)"/>.
/// </summary>
public sealed class WryTrayIconCreateOptions
{
    /// <summary>Tooltip text shown when hovering. Null = no tooltip.</summary>
    public string? Tooltip { get; set; }

    /// <summary>Tray icon title. macOS only - displayed next to the icon.</summary>
    public string? Title { get; set; }

    /// <summary>Encoded image file bytes (PNG, ICO, JPEG, BMP, GIF) for the tray icon.</summary>
    public byte[]? IconData { get; set; }

    /// <summary>Context menu. The menu is consumed at creation time - do not reuse.</summary>
    public WryTrayMenu? Menu { get; set; }

    /// <summary>Show the context menu on left-click. Default true.</summary>
    public bool MenuOnLeftClick { get; set; } = true;

    /// <summary>Whether the tray icon is visible. Default true.</summary>
    public bool Visible { get; set; } = true;

    /// <summary>macOS only. Treat the icon as a template image (auto-colored to match system theme).</summary>
    public bool IconIsTemplate { get; set; }
}

/// <summary>
/// A system tray icon. Create with <see cref="WryApp.CreateTrayIcon(WryTrayIconCreateOptions)"/>
/// and attach event handlers. Properties are thread-safe and can be set from
/// any thread after the event loop starts.
/// </summary>
public sealed class WryTrayIcon
{
    private readonly WryApp _app;
    private nuint _trayId;
    private nint _nativePtr; // set once materialized in the event loop
    private GCHandle _gcHandle;

    internal WryTrayIcon(WryApp app, nuint trayId)
    {
        _app = app;
        _trayId = trayId;
        _gcHandle = GCHandle.Alloc(this);
    }

    internal void SetTrayId(nuint id) => _trayId = id;

    internal nint GCHandlePtr => GCHandle.ToIntPtr(_gcHandle);

    /// <summary>Whether the tray icon has been materialized (post-run).</summary>
    public bool IsLive => _nativePtr != 0;

    // =======================================================================
    // Events
    // =======================================================================

    /// <summary>
    /// Raised when the tray icon receives a mouse event (click, double-click,
    /// enter, move, leave).
    /// </summary>
    public event EventHandler<TrayIconEventArgs>? TrayEvent;

    /// <summary>
    /// Raised when a context menu item is clicked.
    /// </summary>
    public event EventHandler<TrayMenuItemClickedEventArgs>? MenuItemClicked;

    // =======================================================================
    // Properties (set after the tray is materialized via RunOnMainThread)
    // =======================================================================

    /// <summary>Set the tooltip text shown when hovering over the tray icon.</summary>
    public string? Tooltip
    {
        set
        {
            if (value is null) return;
            RunOnMainThread(t => NativeMethods.wry_tray_set_tooltip(t._nativePtr, value));
        }
    }

    /// <summary>Set the tray icon title (macOS only - displayed next to the icon).</summary>
    public string? Title
    {
        set
        {
            if (value is null) return;
            RunOnMainThread(t => NativeMethods.wry_tray_set_title(t._nativePtr, value));
        }
    }

    /// <summary>
    /// Set the context menu. The menu is consumed - do not reuse or dispose it
    /// after setting this property. Set to null to remove the menu.
    /// </summary>
    public WryTrayMenu? Menu
    {
        set
        {
            var handle = value?.ConsumeHandle() ?? 0;
            RunOnMainThread(t => NativeMethods.wry_tray_set_menu(t._nativePtr, handle));
        }
    }

    /// <summary>
    /// When true, the context menu opens on left-click instead of the default
    /// right-click behavior.
    /// </summary>
    public bool MenuOnLeftClick
    {
        set => RunOnMainThread(t => NativeMethods.wry_tray_set_menu_on_left_click(t._nativePtr, value));
    }

    /// <summary>Set the visibility of the tray icon.</summary>
    public bool Visible
    {
        set => RunOnMainThread(t => NativeMethods.wry_tray_set_visible(t._nativePtr, value));
    }

    /// <summary>
    /// macOS only. When true, the icon is treated as a template image
    /// (automatically colored to match the system theme).
    /// </summary>
    public bool IconIsTemplate
    {
        set => RunOnMainThread(t => NativeMethods.wry_tray_set_icon_as_template(t._nativePtr, value));
    }

    // =======================================================================
    // Icon setters
    // =======================================================================

    /// <summary>
    /// Set the tray icon from raw RGBA pixel data.
    /// </summary>
    /// <param name="rgba">RGBA pixel data (4 bytes per pixel).</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    public unsafe void SetIcon(byte[] rgba, int width, int height)
    {
        var copy = (byte[])rgba.Clone();
        RunOnMainThread(t =>
        {
            fixed (byte* ptr = copy)
                NativeMethods.wry_tray_set_icon(t._nativePtr, (nint)ptr, copy.Length, width, height);
        });
    }

    /// <summary>
    /// Set the tray icon from encoded image bytes (PNG, ICO, JPEG, BMP, GIF).
    /// The native side decodes the image automatically.
    /// </summary>
    /// <param name="data">Encoded image file bytes.</param>
    public unsafe void SetIconFromBytes(byte[] data)
    {
        var copy = (byte[])data.Clone();
        RunOnMainThread(t =>
        {
            fixed (byte* ptr = copy)
                NativeMethods.wry_tray_set_icon_from_bytes(t._nativePtr, (nint)ptr, copy.Length);
        });
    }

    // =======================================================================
    // Cross-thread dispatch
    // =======================================================================

    private bool IsOnMainThread => Environment.CurrentManagedThreadId == _app.MainThreadId;

    private void RunOnMainThread(Action<WryTrayIcon> action)
    {
        if (IsOnMainThread)
            action(this);
        else
            Dispatch(action);
    }

    internal unsafe void Dispatch(Action<WryTrayIcon> action)
    {
        var captured = (Tray: this, Action: action);
        var handle = GCHandle.Alloc(captured);
        delegate* unmanaged[Cdecl]<nint, nint, void> fp = &DispatchBridge;
        NativeMethods.wry_tray_dispatch(_app.Handle, _trayId, (nint)fp, GCHandle.ToIntPtr(handle));
    }

    // =======================================================================
    // Remove
    // =======================================================================

    /// <summary>
    /// Remove this tray icon. After removal the tray icon is destroyed and
    /// should not be used further. If this was the last tray icon and no
    /// windows remain, the application will exit.
    /// </summary>
    public void Remove()
    {
        NativeMethods.wry_tray_remove(_app.Handle, _trayId);
    }

    // =======================================================================
    // Internal: pointer capture
    // =======================================================================

    /// <summary>Queue a dispatch to capture the native tray pointer after Init.</summary>
    internal unsafe void QueuePointerCapture()
    {
        delegate* unmanaged[Cdecl]<nint, nint, void> fp = &PointerCaptureBridge;
        NativeMethods.wry_tray_dispatch(_app.Handle, _trayId, (nint)fp, GCHandlePtr);
    }

    /// <summary>Called by WryApp after Run() returns.</summary>
    internal void OnAppRunCompleted()
    {
        _nativePtr = 0;
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

    private static WryTrayIcon? Recover(nint ctx)
    {
        if (ctx == 0) return null;
        var handle = GCHandle.FromIntPtr(ctx);
        return handle.Target as WryTrayIcon;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void PointerCaptureBridge(nint trayPtr, nint ctx)
    {
        if (Recover(ctx) is { } tray)
            tray._nativePtr = trayPtr;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    internal static void TrayEventBridge(
        int eventType, double x, double y,
        double iconX, double iconY, uint iconW, uint iconH,
        int button, int buttonState, nint ctx)
    {
        if (Recover(ctx) is { } tray)
        {
            var args = new TrayIconEventArgs(
                (TrayIconEventType)eventType,
                x, y,
                iconX, iconY, iconW, iconH,
                (TrayMouseButton)button,
                (TrayMouseButtonState)buttonState);
            tray.TrayEvent?.Invoke(tray, args);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    internal static void MenuEventBridge(nint itemId, nint ctx)
    {
        if (Recover(ctx) is { } tray)
        {
            var id = Marshal.PtrToStringUTF8(itemId) ?? "";
            tray.MenuItemClicked?.Invoke(tray, new TrayMenuItemClickedEventArgs(id));
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void DispatchBridge(nint trayPtr, nint ctx)
    {
        if (ctx == 0) return;
        var handle = GCHandle.FromIntPtr(ctx);
        try
        {
            if (handle.Target is (WryTrayIcon tray, Action<WryTrayIcon> action))
            {
                if (tray._nativePtr == 0)
                    tray._nativePtr = trayPtr;
                action.Invoke(tray);
            }
        }
        finally
        {
            handle.Free();
        }
    }
}
