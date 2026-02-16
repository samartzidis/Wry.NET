namespace Wry.NET;

/// <summary>
/// Builder for a tray icon context menu. Add items, check items, separators,
/// and submenus, then assign to <see cref="WryTrayIcon.Menu"/>.
/// </summary>
/// <example>
/// <code>
/// var menu = new WryTrayMenu();
/// menu.AddItem("open", "Open Window");
/// menu.AddSeparator();
/// var sub = menu.AddSubmenu("Options");
/// sub.AddCheckItem("dark", "Dark Mode");
/// menu.AddSeparator();
/// menu.AddItem("quit", "Quit");
/// tray.Menu = menu;
/// </code>
/// </example>
public sealed class WryTrayMenu : IDisposable
{
    internal nint Handle { get; private set; }
    private readonly bool _owned;

    /// <summary>
    /// Create a new empty tray context menu.
    /// </summary>
    public WryTrayMenu()
    {
        Handle = NativeMethods.wry_tray_menu_new();
        if (Handle == 0)
            throw new InvalidOperationException("Failed to create native tray menu.");
        _owned = true;
    }

    /// <summary>
    /// Wraps a submenu pointer returned by AddSubmenu. Not owned - freed with the parent.
    /// </summary>
    private WryTrayMenu(nint handle)
    {
        Handle = handle;
        _owned = false;
    }

    private void EnsureValid()
    {
        if (Handle == 0)
            throw new ObjectDisposedException(nameof(WryTrayMenu),
                "This menu has been disposed or consumed by a WryTrayIcon.");
    }

    /// <summary>
    /// Add a regular menu item.
    /// </summary>
    /// <param name="id">Unique string ID for this item (used in menu click events).</param>
    /// <param name="label">Display text for the item.</param>
    /// <param name="enabled">Whether the item is enabled (default true).</param>
    public void AddItem(string id, string label, bool enabled = true)
    {
        EnsureValid();
        NativeMethods.wry_tray_menu_add_item(Handle, id, label, enabled);
    }

    /// <summary>
    /// Add a checkable menu item.
    /// </summary>
    /// <param name="id">Unique string ID for this item (used in menu click events).</param>
    /// <param name="label">Display text for the item.</param>
    /// <param name="checked">Initial checked state (default false).</param>
    /// <param name="enabled">Whether the item is enabled (default true).</param>
    public void AddCheckItem(string id, string label, bool @checked = false, bool enabled = true)
    {
        EnsureValid();
        NativeMethods.wry_tray_menu_add_check_item(Handle, id, label, @checked, enabled);
    }

    /// <summary>
    /// Add a separator line to the menu.
    /// </summary>
    public void AddSeparator()
    {
        EnsureValid();
        NativeMethods.wry_tray_menu_add_separator(Handle);
    }

    /// <summary>
    /// Add a submenu. Returns a <see cref="WryTrayMenu"/> for the submenu
    /// that you can add items to. Do not dispose the returned submenu - it is
    /// owned by its parent.
    /// </summary>
    /// <param name="label">Display text for the submenu.</param>
    /// <param name="enabled">Whether the submenu is enabled (default true).</param>
    public WryTrayMenu AddSubmenu(string label, bool enabled = true)
    {
        EnsureValid();
        var sub = NativeMethods.wry_tray_menu_add_submenu(Handle, label, enabled);
        if (sub == 0)
            throw new InvalidOperationException("Failed to create native submenu.");
        return new WryTrayMenu(sub);
    }

    /// <summary>
    /// Transfer ownership of the native handle to the caller (native side).
    /// After this call, Dispose is a no-op.
    /// </summary>
    internal nint ConsumeHandle()
    {
        var h = Handle;
        Handle = 0;
        return h;
    }

    public void Dispose()
    {
        if (_owned && Handle != 0)
        {
            NativeMethods.wry_tray_menu_destroy(Handle);
            Handle = 0;
        }
    }
}
