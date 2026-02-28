namespace Wry.NET;

// ---------------------------------------------------------------------------
// Enums
// ---------------------------------------------------------------------------

/// <summary>
/// WebView theme. Windows only.
/// </summary>
public enum WryTheme
{
    /// <summary>Follow system preference.</summary>
    Auto = 0,
    /// <summary>Dark theme.</summary>
    Dark = 1,
    /// <summary>Light theme.</summary>
    Light = 2,
}

/// <summary>
/// Scrollbar style. Windows only.
/// </summary>
public enum WryScrollBarStyle
{
    /// <summary>Browser default scrollbar style.</summary>
    Default = 0,
    /// <summary>Fluent UI style overlay scrollbars.</summary>
    FluentOverlay = 1,
}

/// <summary>
/// Background throttling policy for when the webview is not visible.
/// macOS 14+ / iOS 17+ only.
/// </summary>
public enum WryBackgroundThrottlingPolicy
{
    /// <summary>Background throttling is disabled.</summary>
    Disabled = 0,
    /// <summary>Fully suspend tasks when not in a window (default browser behavior).</summary>
    Suspend = 1,
    /// <summary>Limit processing but don't fully suspend tasks.</summary>
    Throttle = 2,
}

/// <summary>
/// Drag-drop event type, indicating which phase of a drag-drop operation occurred.
/// </summary>
public enum DragDropEventType
{
    /// <summary>A drag operation has entered the webview.</summary>
    Enter = 0,
    /// <summary>A drag operation is moving over the webview.</summary>
    Over = 1,
    /// <summary>File(s) have been dropped onto the webview.</summary>
    Drop = 2,
    /// <summary>The drag operation was cancelled or left the webview.</summary>
    Leave = 3,
}

/// <summary>
/// Page load event type, indicating whether a page load started or finished.
/// </summary>
public enum WryPageLoadEvent
{
    /// <summary>The page has started loading.</summary>
    Started = 0,
    /// <summary>The page has finished loading.</summary>
    Finished = 1,
}

/// <summary>
/// Type of tray icon event.
/// </summary>
public enum TrayIconEventType
{
    /// <summary>Single click on the tray icon.</summary>
    Click = 0,
    /// <summary>Double click on the tray icon.</summary>
    DoubleClick = 1,
    /// <summary>Mouse cursor entered the tray icon area.</summary>
    Enter = 2,
    /// <summary>Mouse cursor moved within the tray icon area.</summary>
    Move = 3,
    /// <summary>Mouse cursor left the tray icon area.</summary>
    Leave = 4,
}

/// <summary>
/// Mouse button involved in a tray icon event.
/// </summary>
public enum TrayMouseButton
{
    /// <summary>Left mouse button.</summary>
    Left = 0,
    /// <summary>Right mouse button.</summary>
    Right = 1,
    /// <summary>Middle mouse button.</summary>
    Middle = 2,
}

/// <summary>
/// State of the mouse button in a tray icon click event.
/// </summary>
public enum TrayMouseButtonState
{
    /// <summary>Button was released.</summary>
    Up = 0,
    /// <summary>Button was pressed.</summary>
    Down = 1,
}

/// <summary>
/// Dialog kind (icon / severity) for message, ask, and confirm dialogs.
/// </summary>
public enum WryDialogKind
{
    /// <summary>Information.</summary>
    Info = 0,
    /// <summary>Warning.</summary>
    Warning = 1,
    /// <summary>Error.</summary>
    Error = 2,
}

/// <summary>
/// Button set for message dialogs.
/// </summary>
public enum WryDialogButtons
{
    /// <summary>Single Ok button.</summary>
    Ok = 0,
    /// <summary>Ok and Cancel.</summary>
    OkCancel = 1,
    /// <summary>Yes and No.</summary>
    YesNo = 2,
    /// <summary>Yes, No, and Cancel.</summary>
    YesNoCancel = 3,
}

// ---------------------------------------------------------------------------
// Structs
// ---------------------------------------------------------------------------

/// <summary>
/// RGBA color with 8-bit channels.
/// </summary>
public readonly record struct WryColor(byte R, byte G, byte B, byte A = 255)
{
    public static WryColor White => new(255, 255, 255);
    public static WryColor Black => new(0, 0, 0);
    public static WryColor Transparent => new(0, 0, 0, 0);
}

/// <summary>
/// Information about a display monitor.
/// </summary>
public readonly record struct MonitorInfo(
    int X,
    int Y,
    int Width,
    int Height,
    double ScaleFactor
);

// ---------------------------------------------------------------------------
// Event args
// ---------------------------------------------------------------------------

/// <summary>
/// Event args for IPC messages received from the webview.
/// </summary>
public sealed class IpcMessageEventArgs : EventArgs
{
    /// <summary>The message body sent from JavaScript.</summary>
    public string Message { get; }

    /// <summary>The origin URL of the page that sent the message.</summary>
    public string Url { get; }

    public IpcMessageEventArgs(string message, string url)
    {
        Message = message;
        Url = url;
    }
}

/// <summary>
/// Event args for the close-requested event. Set <see cref="Cancel"/> to
/// true to prevent the window from closing.
/// </summary>
public sealed class CloseRequestedEventArgs : EventArgs
{
    /// <summary>Set to true to prevent the window from closing.</summary>
    public bool Cancel { get; set; }
}

/// <summary>
/// Event args raised when all windows have closed or when
/// <see cref="WryApp.Exit"/> is called. Set <see cref="Cancel"/> to true to
/// keep the event loop running (e.g. for tray-icon-only mode). If no handler
/// cancels, the application exits and any remaining tray icons are removed
/// automatically.
/// </summary>
public sealed class ExitRequestedEventArgs : EventArgs
{
    /// <summary>
    /// The exit code if this was a programmatic exit via <see cref="WryApp.Exit"/>,
    /// or null if the last window was closed by the user.
    /// </summary>
    public int? ExitCode { get; }

    /// <summary>Set to true to prevent the application from exiting.</summary>
    public bool Cancel { get; set; }

    internal ExitRequestedEventArgs(int? exitCode) => ExitCode = exitCode;
}

/// <summary>
/// Event args for window resize events.
/// </summary>
public sealed class SizeChangedEventArgs : EventArgs
{
    /// <summary>New width in physical pixels.</summary>
    public int Width { get; }
    /// <summary>New height in physical pixels.</summary>
    public int Height { get; }

    public SizeChangedEventArgs(int width, int height)
    {
        Width = width;
        Height = height;
    }
}

/// <summary>
/// Event args for window move events.
/// </summary>
public sealed class PositionChangedEventArgs : EventArgs
{
    /// <summary>New X position.</summary>
    public int X { get; }
    /// <summary>New Y position.</summary>
    public int Y { get; }

    public PositionChangedEventArgs(int x, int y)
    {
        X = x;
        Y = y;
    }
}

/// <summary>
/// Event args for window focus change events.
/// </summary>
public sealed class FocusChangedEventArgs : EventArgs
{
    /// <summary>True if the window gained focus, false if lost.</summary>
    public bool Focused { get; }

    public FocusChangedEventArgs(bool focused) => Focused = focused;
}

/// <summary>
/// Event args for navigation requests. Set <see cref="Cancel"/> to true
/// to block the navigation.
/// </summary>
public sealed class NavigatingEventArgs : EventArgs
{
    /// <summary>The URL the webview is about to navigate to.</summary>
    public string Url { get; }

    /// <summary>Set to true to block this navigation.</summary>
    public bool Cancel { get; set; }

    public NavigatingEventArgs(string url) => Url = url;
}

/// <summary>
/// Event args for drag-drop events on the webview.
/// Set <see cref="BlockDefault"/> to true to suppress the OS default behavior
/// (which would otherwise allow native file drop on &lt;input type="file"&gt;).
/// </summary>
public sealed class DragDropEventArgs : EventArgs
{
    /// <summary>The drag-drop event phase.</summary>
    public DragDropEventType Type { get; }

    /// <summary>
    /// File paths being dragged or dropped. Non-null for
    /// <see cref="DragDropEventType.Enter"/> and <see cref="DragDropEventType.Drop"/>.
    /// </summary>
    public string[]? Paths { get; }

    /// <summary>Cursor X position relative to the webview.</summary>
    public int X { get; }

    /// <summary>Cursor Y position relative to the webview.</summary>
    public int Y { get; }

    /// <summary>Set to true to block the OS default drag-drop behavior.</summary>
    public bool BlockDefault { get; set; }

    public DragDropEventArgs(DragDropEventType type, string[]? paths, int x, int y)
    {
        Type = type;
        Paths = paths;
        X = x;
        Y = y;
    }
}

/// <summary>
/// Event args for page load events (started/finished).
/// </summary>
public sealed class PageLoadEventArgs : EventArgs
{
    /// <summary>Whether the page started or finished loading.</summary>
    public WryPageLoadEvent Event { get; }

    /// <summary>The URL that is loading or has loaded.</summary>
    public string Url { get; }

    public PageLoadEventArgs(WryPageLoadEvent @event, string url)
    {
        Event = @event;
        Url = url;
    }
}

/// <summary>
/// Represents an incoming custom protocol request.
/// </summary>
public sealed class ProtocolRequest
{
    /// <summary>Full request URI (e.g. "app://localhost/index.html").</summary>
    public string Url { get; }

    /// <summary>HTTP method (e.g. "GET", "POST").</summary>
    public string Method { get; }

    /// <summary>Request headers.</summary>
    public IReadOnlyDictionary<string, string> Headers { get; }

    /// <summary>Request body bytes (empty for GET requests).</summary>
    public byte[] Body { get; }

    public ProtocolRequest(string url, string method, IReadOnlyDictionary<string, string> headers, byte[] body)
    {
        Url = url;
        Method = method;
        Headers = headers;
        Body = body;
    }
}

/// <summary>
/// Response for a custom protocol request.
/// </summary>
public sealed class ProtocolResponse
{
    /// <summary>Response body bytes.</summary>
    public byte[] Data { get; set; } = [];

    /// <summary>MIME content type (e.g. "text/html").</summary>
    public string ContentType { get; set; } = "application/octet-stream";

    /// <summary>HTTP status code.</summary>
    public int StatusCode { get; set; } = 200;

    /// <summary>
    /// Additional response headers. Content-Type is set automatically from
    /// <see cref="ContentType"/>; use this for headers like Cache-Control,
    /// Access-Control-Allow-Origin, etc.
    /// </summary>
    public Dictionary<string, string>? Headers { get; set; }
}

/// <summary>
/// Event args for tray icon events (click, double-click, mouse enter/move/leave).
/// </summary>
public sealed class TrayIconEventArgs : EventArgs
{
    /// <summary>The type of tray icon event.</summary>
    public TrayIconEventType EventType { get; }

    /// <summary>Mouse X position in physical pixels.</summary>
    public double X { get; }

    /// <summary>Mouse Y position in physical pixels.</summary>
    public double Y { get; }

    /// <summary>Tray icon rect X position.</summary>
    public double IconX { get; }

    /// <summary>Tray icon rect Y position.</summary>
    public double IconY { get; }

    /// <summary>Tray icon rect width.</summary>
    public uint IconWidth { get; }

    /// <summary>Tray icon rect height.</summary>
    public uint IconHeight { get; }

    /// <summary>Which mouse button was involved (for Click/DoubleClick events).</summary>
    public TrayMouseButton Button { get; }

    /// <summary>Mouse button state (for Click events).</summary>
    public TrayMouseButtonState ButtonState { get; }

    public TrayIconEventArgs(
        TrayIconEventType eventType,
        double x, double y,
        double iconX, double iconY, uint iconWidth, uint iconHeight,
        TrayMouseButton button, TrayMouseButtonState buttonState)
    {
        EventType = eventType;
        X = x;
        Y = y;
        IconX = iconX;
        IconY = iconY;
        IconWidth = iconWidth;
        IconHeight = iconHeight;
        Button = button;
        ButtonState = buttonState;
    }
}

/// <summary>
/// Event args for tray context menu item click events.
/// </summary>
public sealed class TrayMenuItemClickedEventArgs : EventArgs
{
    /// <summary>The string ID of the menu item that was clicked.</summary>
    public string ItemId { get; }

    public TrayMenuItemClickedEventArgs(string itemId)
    {
        ItemId = itemId;
    }
}
