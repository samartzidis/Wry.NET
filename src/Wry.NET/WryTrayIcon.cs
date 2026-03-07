using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Wry.NET;

public sealed class WryTrayIcon
{
    private readonly WryApp _app;
    private readonly nuint _trayId;
    private nint _nativePtr; // set once materialized in the event loop
    private GCHandle _gcHandle;

    internal WryTrayIcon(WryApp app, nuint trayId)
    {
        _app = app;
        _trayId = trayId;
        _gcHandle = GCHandle.Alloc(this);
    }

    private nint GCHandlePtr => GCHandle.ToIntPtr(_gcHandle);

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
    // Properties (set before or after app.Run())
    // =======================================================================

    /// <summary>Set the tooltip text shown when hovering over the tray icon.</summary>
    public string? Tooltip
    {
        set
        {
            if (value is null) return;
            if (IsLive)
                RunOnMainThread(t => NativeMethods.wry_tray_set_tooltip_direct(t._nativePtr, value));
            else
                NativeMethods.wry_tray_set_tooltip(_app.Handle, _trayId, value);
        }
    }

    /// <summary>Set the tray icon title (macOS only - displayed next to the icon).</summary>
    public string? Title
    {
        set
        {
            if (value is null) return;
            if (IsLive)
                RunOnMainThread(t => NativeMethods.wry_tray_set_title_direct(t._nativePtr, value));
            else
                NativeMethods.wry_tray_set_title(_app.Handle, _trayId, value);
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
            if (IsLive)
                RunOnMainThread(t => NativeMethods.wry_tray_set_menu_direct(t._nativePtr, handle));
            else
                NativeMethods.wry_tray_set_menu(_app.Handle, _trayId, handle);
        }
    }

    /// <summary>
    /// When true, the context menu opens on left-click instead of the default
    /// right-click behavior.
    /// </summary>
    public bool MenuOnLeftClick
    {
        set
        {
            if (IsLive)
                RunOnMainThread(t => NativeMethods.wry_tray_set_menu_on_left_click_direct(t._nativePtr, value));
            else
                NativeMethods.wry_tray_set_menu_on_left_click(_app.Handle, _trayId, value);
        }
    }

    /// <summary>Set the visibility of the tray icon.</summary>
    public bool Visible
    {
        set
        {
            if (IsLive)
                RunOnMainThread(t => NativeMethods.wry_tray_set_visible_direct(t._nativePtr, value));
            else
                NativeMethods.wry_tray_set_visible(_app.Handle, _trayId, value);
        }
    }

    /// <summary>
    /// macOS only. When true, the icon is treated as a template image
    /// (automatically colored to match the system theme).
    /// </summary>
    public bool IconIsTemplate
    {
        set
        {
            if (IsLive)
                RunOnMainThread(t => NativeMethods.wry_tray_set_icon_as_template_direct(t._nativePtr, value));
            else
                NativeMethods.wry_tray_set_icon_as_template(_app.Handle, _trayId, value);
        }
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
        if (IsLive)
        {
            var copy = (byte[])rgba.Clone();
            RunOnMainThread(t =>
            {
                fixed (byte* ptr = copy)
                    NativeMethods.wry_tray_set_icon_direct(t._nativePtr, (nint)ptr, copy.Length, width, height);
            });
        }
        else
        {
            fixed (byte* ptr = rgba)
                NativeMethods.wry_tray_set_icon(_app.Handle, _trayId, (nint)ptr, rgba.Length, width, height);
        }
    }

    /// <summary>
    /// Set the tray icon from encoded image bytes (PNG, ICO, JPEG, BMP, GIF).
    /// The native side decodes the image automatically.
    /// </summary>
    /// <param name="data">Encoded image file bytes.</param>
    public unsafe void SetIconFromBytes(byte[] data)
    {
        if (IsLive)
        {
            var copy = (byte[])data.Clone();
            RunOnMainThread(t =>
            {
                fixed (byte* ptr = copy)
                    NativeMethods.wry_tray_set_icon_from_bytes_direct(t._nativePtr, (nint)ptr, copy.Length);
            });
        }
        else
        {
            fixed (byte* ptr = data)
                NativeMethods.wry_tray_set_icon_from_bytes(_app.Handle, _trayId, (nint)ptr, data.Length);
        }
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
    // Internal: callback registration & pointer capture
    // =======================================================================

    /// <summary>Register native event callbacks. Called by WryApp before Run().</summary>
    internal unsafe void RegisterNativeCallbacks()
    {
        var ctx = GCHandlePtr;

        {
            delegate* unmanaged[Cdecl]<int, double, double, double, double, uint, uint, int, int, nint, void> fp = &TrayEventBridge;
            NativeMethods.wry_tray_on_event(_app.Handle, _trayId, (nint)fp, ctx);
        }

        {
            delegate* unmanaged[Cdecl]<nint, nint, void> fp = &MenuEventBridge;
            NativeMethods.wry_tray_on_menu_event(_app.Handle, _trayId, (nint)fp, ctx);
        }
    }

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

    private void EnsureLive([CallerMemberName] string? caller = null)
    {
        if (!IsLive)
            throw new InvalidOperationException(
                $"Cannot call {caller} before the tray icon is live. " +
                "Use this method from an event handler after WryApp.Run() starts.");
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
    private static void TrayEventBridge(
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
    private static void MenuEventBridge(nint itemId, nint ctx)
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
