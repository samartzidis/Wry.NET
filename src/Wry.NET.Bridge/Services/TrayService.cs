using Wry.NET;

namespace Wry.NET.Bridge.Services;

/// <summary>
/// Event payload when a tray context menu item is clicked.
/// Subscribe in JS via <c>events.onTrayMenuItemClicked(cb)</c> or <c>events.on('trayMenuItemClicked', cb)</c>.
/// </summary>
[BridgeEvent("trayMenuItemClicked")]
public class TrayMenuItemClickedEvent
{
    /// <summary>Tray ID (same value returned by GetTrayIds).</summary>
    public long TrayId { get; set; }

    /// <summary>Menu item string ID that was clicked.</summary>
    public string ItemId { get; set; } = "";
}

/// <summary>
/// Event payload for tray icon mouse events (click, double-click, enter, move, leave).
/// Subscribe in JS via <c>events.onTrayIconEvent(cb)</c>.
/// </summary>
[BridgeEvent("trayIconEvent")]
public class TrayIconEventPayload
{
    public long TrayId { get; set; }
    /// <summary>One of: Click, DoubleClick, Enter, Move, Leave.</summary>
    public string Type { get; set; } = "";
    public TrayIconEventPosition Position { get; set; } = new();
    public TrayIconEventRect Rect { get; set; } = new();
    /// <summary>Left, Right, or Middle (only for Click/DoubleClick).</summary>
    public string? Button { get; set; }
    /// <summary>Up or Down (only for Click).</summary>
    public string? ButtonState { get; set; }
}

public class TrayIconEventPosition
{
    public double X { get; set; }
    public double Y { get; set; }
}

public class TrayIconEventRect
{
    public TrayIconEventPosition Position { get; set; } = new();
    public TrayIconEventSize Size { get; set; } = new();
}

public class TrayIconEventSize
{
    public double Width { get; set; }
    public double Height { get; set; }
}

/// <summary>
/// Bridge service exposing tray icon and menu API to the frontend.
/// Trays are created from C# (e.g. in Program.cs before Run). Use GetTrayIds to get
/// tray IDs, then call mutation methods. Subscribe to tray:MenuItemClicked for clicks.
/// </summary>
[BridgeService]
public class TrayService
{
    private readonly WryApp _app;
    private readonly WryBridge _bridge;

    public TrayService(WryApp app, WryBridge bridge)
    {
        _app = app;
        _bridge = bridge;
        foreach (var tray in app.TrayIcons)
        {
            var trayId = (long)tray.TrayId;
            tray.MenuItemClicked += (_, e) => _bridge.Emit("trayMenuItemClicked", new TrayMenuItemClickedEvent
            {
                TrayId = trayId,
                ItemId = e.ItemId
            });
            tray.TrayEvent += (_, e) => _bridge.Emit("trayIconEvent", new TrayIconEventPayload
            {
                TrayId = trayId,
                Type = e.EventType.ToString(),
                Position = new TrayIconEventPosition { X = e.X, Y = e.Y },
                Rect = new TrayIconEventRect
                {
                    Position = new TrayIconEventPosition { X = e.IconX, Y = e.IconY },
                    Size = new TrayIconEventSize { Width = e.IconWidth, Height = e.IconHeight }
                },
                Button = e.EventType is TrayIconEventType.Click or TrayIconEventType.DoubleClick ? e.Button.ToString() : null,
                ButtonState = e.EventType == TrayIconEventType.Click ? e.ButtonState.ToString() : null
            });
        }
    }

    /// <summary>Returns IDs of all tray icons (created from C# before Run).</summary>
    public long[] GetTrayIds()
    {
        return _app.TrayIcons.Select(t => (long)t.TrayId).ToArray();
    }

    private WryTrayIcon? GetTray(long trayId)
    {
        return _app.TrayIcons.FirstOrDefault(t => (long)t.TrayId == trayId);
    }

    /// <summary>Set the tooltip text for the tray icon.</summary>
    public void SetTooltip(long trayId, string? tooltip)
    {
        var t = GetTray(trayId);
        if (t != null) t.Tooltip = tooltip;
    }

    /// <summary>Set the tray icon title (macOS only).</summary>
    public void SetTitle(long trayId, string? title)
    {
        var t = GetTray(trayId);
        if (t != null) t.Title = title;
    }

    /// <summary>Show or hide the tray icon.</summary>
    public void SetVisible(long trayId, bool visible)
    {
        var t = GetTray(trayId);
        if (t != null) t.Visible = visible;
    }

    /// <summary>When true, context menu opens on left-click instead of right-click.</summary>
    public void SetMenuOnLeftClick(long trayId, bool menuOnLeftClick)
    {
        var t = GetTray(trayId);
        if (t != null) t.MenuOnLeftClick = menuOnLeftClick;
    }

    /// <summary>macOS only. When true, icon is a template image (follows system theme).</summary>
    public void SetIconAsTemplate(long trayId, bool isTemplate)
    {
        var t = GetTray(trayId);
        if (t != null) t.IconIsTemplate = isTemplate;
    }

    /// <summary>Set the tray icon from base64-encoded image data (e.g. PNG).</summary>
    public void SetIconFromBase64(long trayId, string? iconBase64)
    {
        if (string.IsNullOrEmpty(iconBase64)) return;
        var t = GetTray(trayId);
        if (t == null) return;
        try
        {
            var bytes = Convert.FromBase64String(iconBase64);
            t.SetIconFromBytes(bytes);
        }
        catch (FormatException) { /* ignore invalid base64 */ }
    }

    /// <summary>Get the display text of a menu item.</summary>
    public string? GetMenuItemText(long trayId, string itemId)
    {
        return GetTray(trayId)?.GetMenuItemText(itemId);
    }

    /// <summary>Set the display text of a menu item.</summary>
    public void SetMenuItemText(long trayId, string itemId, string text)
    {
        GetTray(trayId)?.SetMenuItemText(itemId, text);
    }

    /// <summary>Returns whether a menu item is enabled.</summary>
    public bool IsMenuItemEnabled(long trayId, string itemId)
    {
        return GetTray(trayId)?.IsMenuItemEnabled(itemId) ?? false;
    }

    /// <summary>Enable or disable a menu item.</summary>
    public void SetMenuItemEnabled(long trayId, string itemId, bool enabled)
    {
        GetTray(trayId)?.SetMenuItemEnabled(itemId, enabled);
    }

    /// <summary>Returns whether a check menu item is checked.</summary>
    public bool IsMenuItemChecked(long trayId, string itemId)
    {
        return GetTray(trayId)?.IsMenuItemChecked(itemId) ?? false;
    }

    /// <summary>Set the checked state of a check menu item.</summary>
    public void SetMenuItemChecked(long trayId, string itemId, bool @checked)
    {
        GetTray(trayId)?.SetMenuItemChecked(itemId, @checked);
    }

    /// <summary>Append a regular menu item. Use null or empty parentId for top-level.</summary>
    public void AppendMenuItem(long trayId, string? parentId, string id, string label, bool enabled = true)
    {
        GetTray(trayId)?.AppendMenuItem(parentId, id, label, enabled);
    }

    /// <summary>Append a check menu item. Use null or empty parentId for top-level.</summary>
    public void AppendCheckMenuItem(long trayId, string? parentId, string id, string label, bool @checked = false, bool enabled = true)
    {
        GetTray(trayId)?.AppendCheckMenuItem(parentId, id, label, @checked, enabled);
    }

    /// <summary>Append a submenu. Use null or empty parentId for top-level.</summary>
    public void AppendSubmenu(long trayId, string? parentId, string id, string label, bool enabled = true)
    {
        GetTray(trayId)?.AppendSubmenu(parentId, id, label, enabled);
    }

    /// <summary>Append a separator. Use null or empty parentId for top-level.</summary>
    public void AppendSeparator(long trayId, string? parentId = null)
    {
        GetTray(trayId)?.AppendSeparator(parentId);
    }

    /// <summary>Remove a menu item by ID.</summary>
    public void RemoveMenuItem(long trayId, string itemId)
    {
        GetTray(trayId)?.RemoveMenuItem(itemId);
    }

    /// <summary>Remove a menu item at position. Use null or empty parentId for top-level.</summary>
    public void RemoveMenuItemAt(long trayId, string? parentId, int position)
    {
        GetTray(trayId)?.RemoveMenuItemAt(parentId, position);
    }
}
