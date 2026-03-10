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

    /// <summary>Opaque tray ID for use in bridge/JS (e.g. TrayService methods).</summary>
    public nuint TrayId => _trayId;

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
    // Menu item runtime getters/setters (by item string ID)
    // =======================================================================

    /// <summary>
    /// Get the text of a menu item. Returns null if the item ID is not found.
    /// </summary>
    /// <param name="itemId">The string ID of the menu item.</param>
    public string? GetMenuItemText(string itemId)
        => _nativePtr == 0 ? null : NativeMethods.ReadAndFreeNativeString(
            NativeMethods.wry_tray_menu_item_text(_nativePtr, itemId));

    /// <summary>
    /// Set the text of a menu item. No-op if the item ID is not found.
    /// </summary>
    /// <param name="itemId">The string ID of the menu item.</param>
    /// <param name="text">The new display text.</param>
    public void SetMenuItemText(string itemId, string text)
        => RunOnMainThread(t => NativeMethods.wry_tray_menu_item_set_text(t._nativePtr, itemId, text));

    /// <summary>
    /// Returns whether a menu item is enabled.
    /// Returns false if the item ID is not found.
    /// </summary>
    /// <param name="itemId">The string ID of the menu item.</param>
    public bool IsMenuItemEnabled(string itemId)
        => _nativePtr != 0 && NativeMethods.wry_tray_menu_item_is_enabled(_nativePtr, itemId);

    /// <summary>
    /// Enable or disable a menu item. No-op if the item ID is not found.
    /// </summary>
    /// <param name="itemId">The string ID of the menu item.</param>
    /// <param name="enabled">Whether the item should be enabled.</param>
    public void SetMenuItemEnabled(string itemId, bool enabled)
        => RunOnMainThread(t => NativeMethods.wry_tray_menu_item_set_enabled(t._nativePtr, itemId, enabled));

    /// <summary>
    /// Returns whether a check menu item is currently checked.
    /// Returns false if the item ID is not found or is not a check item.
    /// </summary>
    /// <param name="itemId">The string ID of the check menu item.</param>
    public bool IsMenuItemChecked(string itemId)
        => _nativePtr != 0 && NativeMethods.wry_tray_check_item_is_checked(_nativePtr, itemId);

    /// <summary>
    /// Set the checked state of a check menu item.
    /// No-op if the item ID is not found or is not a check item.
    /// </summary>
    /// <param name="itemId">The string ID of the check menu item.</param>
    /// <param name="checked">The new checked state.</param>
    public void SetMenuItemChecked(string itemId, bool @checked)
        => RunOnMainThread(t => NativeMethods.wry_tray_check_item_set_checked(t._nativePtr, itemId, @checked));

    // =======================================================================
    // Dynamic menu item append / insert / remove
    // =======================================================================

    /// <summary>
    /// Append a regular menu item to the live tray menu.
    /// </summary>
    /// <param name="parentId">Parent submenu ID, or null for the top-level menu.</param>
    /// <param name="id">Unique string ID for the new item.</param>
    /// <param name="label">Display text.</param>
    /// <param name="enabled">Whether the item is enabled.</param>
    public void AppendMenuItem(string? parentId, string id, string label, bool enabled = true)
        => RunOnMainThread(t => NativeMethods.wry_tray_menu_item_append(t._nativePtr, parentId, 0, id, label, false, enabled));

    /// <summary>
    /// Append a check menu item to the live tray menu.
    /// </summary>
    /// <param name="parentId">Parent submenu ID, or null for the top-level menu.</param>
    /// <param name="id">Unique string ID for the new item.</param>
    /// <param name="label">Display text.</param>
    /// <param name="checked">Initial checked state.</param>
    /// <param name="enabled">Whether the item is enabled.</param>
    public void AppendCheckMenuItem(string? parentId, string id, string label, bool @checked = false, bool enabled = true)
        => RunOnMainThread(t => NativeMethods.wry_tray_menu_item_append(t._nativePtr, parentId, 1, id, label, @checked, enabled));

    /// <summary>
    /// Append a submenu to the live tray menu.
    /// </summary>
    /// <param name="parentId">Parent submenu ID, or null for the top-level menu.</param>
    /// <param name="id">Unique string ID for the new submenu.</param>
    /// <param name="label">Display text.</param>
    /// <param name="enabled">Whether the submenu is enabled.</param>
    public void AppendSubmenu(string? parentId, string id, string label, bool enabled = true)
        => RunOnMainThread(t => NativeMethods.wry_tray_menu_item_append(t._nativePtr, parentId, 2, id, label, false, enabled));

    /// <summary>
    /// Append a separator to the live tray menu.
    /// </summary>
    /// <param name="parentId">Parent submenu ID, or null for the top-level menu.</param>
    public void AppendSeparator(string? parentId = null)
        => RunOnMainThread(t => NativeMethods.wry_tray_menu_item_append(t._nativePtr, parentId, 3, null, null, false, false));

    /// <summary>
    /// Insert a regular menu item at a position in the live tray menu.
    /// </summary>
    /// <param name="parentId">Parent submenu ID, or null for the top-level menu.</param>
    /// <param name="position">Zero-based insertion index.</param>
    /// <param name="id">Unique string ID for the new item.</param>
    /// <param name="label">Display text.</param>
    /// <param name="enabled">Whether the item is enabled.</param>
    public void InsertMenuItem(string? parentId, int position, string id, string label, bool enabled = true)
        => RunOnMainThread(t => NativeMethods.wry_tray_menu_item_insert(t._nativePtr, parentId, position, 0, id, label, false, enabled));

    /// <summary>
    /// Insert a check menu item at a position in the live tray menu.
    /// </summary>
    /// <param name="parentId">Parent submenu ID, or null for the top-level menu.</param>
    /// <param name="position">Zero-based insertion index.</param>
    /// <param name="id">Unique string ID for the new item.</param>
    /// <param name="label">Display text.</param>
    /// <param name="checked">Initial checked state.</param>
    /// <param name="enabled">Whether the item is enabled.</param>
    public void InsertCheckMenuItem(string? parentId, int position, string id, string label, bool @checked = false, bool enabled = true)
        => RunOnMainThread(t => NativeMethods.wry_tray_menu_item_insert(t._nativePtr, parentId, position, 1, id, label, @checked, enabled));

    /// <summary>
    /// Insert a submenu at a position in the live tray menu.
    /// </summary>
    /// <param name="parentId">Parent submenu ID, or null for the top-level menu.</param>
    /// <param name="position">Zero-based insertion index.</param>
    /// <param name="id">Unique string ID for the new submenu.</param>
    /// <param name="label">Display text.</param>
    /// <param name="enabled">Whether the submenu is enabled.</param>
    public void InsertSubmenu(string? parentId, int position, string id, string label, bool enabled = true)
        => RunOnMainThread(t => NativeMethods.wry_tray_menu_item_insert(t._nativePtr, parentId, position, 2, id, label, false, enabled));

    /// <summary>
    /// Insert a separator at a position in the live tray menu.
    /// </summary>
    /// <param name="parentId">Parent submenu ID, or null for the top-level menu.</param>
    /// <param name="position">Zero-based insertion index.</param>
    public void InsertSeparator(string? parentId, int position)
        => RunOnMainThread(t => NativeMethods.wry_tray_menu_item_insert(t._nativePtr, parentId, position, 3, null, null, false, false));

    /// <summary>
    /// Remove a menu item by its string ID from the live tray menu.
    /// Searches the top-level menu and all submenus.
    /// </summary>
    /// <param name="id">The ID of the item to remove.</param>
    public void RemoveMenuItem(string id)
        => RunOnMainThread(t => NativeMethods.wry_tray_menu_item_remove(t._nativePtr, id));

    /// <summary>
    /// Remove a menu item at a position from the live tray menu.
    /// </summary>
    /// <param name="parentId">Parent submenu ID, or null for the top-level menu.</param>
    /// <param name="position">Zero-based index of the item to remove.</param>
    public void RemoveMenuItemAt(string? parentId, int position)
        => RunOnMainThread(t => NativeMethods.wry_tray_menu_item_remove_at(t._nativePtr, parentId, position));

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
