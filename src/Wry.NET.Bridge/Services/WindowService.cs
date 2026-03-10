using Wry.NET;
using Wry.NET.Bridge;

namespace Wry.NET.Bridge.Services;

/// <summary>
/// Event payload when a window gains or loses focus. Subscribe in JS via <c>onWindowFocusChanged(cb)</c>.
/// </summary>
[BridgeEvent("windowFocusChanged")]
public class WindowFocusChangedEvent
{
    public long WindowId { get; set; }
    public bool Focused { get; set; }
}

/// <summary>
/// Event payload when a window is resized. Subscribe in JS via <c>onWindowResized(cb)</c>.
/// </summary>
[BridgeEvent("windowResized")]
public class WindowResizedEvent
{
    public long WindowId { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

/// <summary>
/// Event payload when a window is moved. Subscribe in JS via <c>onWindowMoved(cb)</c>.
/// </summary>
[BridgeEvent("windowMoved")]
public class WindowMovedEvent
{
    public long WindowId { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
}

/// <summary>
/// Event payload when a window close is requested. Subscribe in JS via <c>onWindowCloseRequested(cb)</c>.
/// </summary>
[BridgeEvent("windowCloseRequested")]
public class WindowCloseRequestedEvent
{
    public long WindowId { get; set; }
}

/// <summary>
/// Size in logical pixels (width, height).
/// </summary>
public class WindowSize
{
    public int Width { get; set; }
    public int Height { get; set; }
}

/// <summary>
/// Position in logical pixels (x, y).
/// </summary>
public class WindowPosition
{
    public int X { get; set; }
    public int Y { get; set; }
}

/// <summary>
/// Bridge service exposing window identity and per-window operations to the frontend.
/// Pass 0 (or omit) for windowId to use the calling window; pass a specific id to target another window (from GetWindowIds).
/// Subscribe to windowFocusChanged, windowResized, windowMoved, windowCloseRequested for window events.
/// </summary>
[BridgeService]
public class WindowService
{
    /// <summary>Use this value (or omit the windowId parameter) to target the window that made the current bridge call.</summary>
    public const long CurrentWindow = 0;

    private readonly WryApp _app;
    private readonly WryBridge _bridge;

    public WindowService(WryApp app, WryBridge bridge)
    {
        _app = app;
        _bridge = bridge;
        _bridge.WindowAttached += OnWindowAttached;
    }

    private void OnWindowAttached(WryWindow window)
    {
        var windowId = (long)window.Id;

        EventHandler<FocusChangedEventArgs>? focusHandler = (_, e) => _bridge.Emit("windowFocusChanged", new WindowFocusChangedEvent { WindowId = windowId, Focused = e.Focused });
        EventHandler<SizeChangedEventArgs>? resizeHandler = (_, e) => _bridge.Emit("windowResized", new WindowResizedEvent { WindowId = windowId, Width = e.Width, Height = e.Height });
        EventHandler<PositionChangedEventArgs>? moveHandler = (_, e) => _bridge.Emit("windowMoved", new WindowMovedEvent { WindowId = windowId, X = e.X, Y = e.Y });
        EventHandler<CloseRequestedEventArgs>? closeHandler = (_, _) => _bridge.Emit("windowCloseRequested", new WindowCloseRequestedEvent { WindowId = windowId });

        window.FocusChanged += focusHandler;
        window.Resized += resizeHandler;
        window.Moved += moveHandler;
        window.CloseRequested += closeHandler;

        window.WindowDestroyed += (_, _) =>
        {
            window.FocusChanged -= focusHandler;
            window.Resized -= resizeHandler;
            window.Moved -= moveHandler;
            window.CloseRequested -= closeHandler;
        };
    }

    /// <summary>Returns the id of the window that sent the current bridge call. Returns 0 if unknown.</summary>
    public long GetCurrentWindowId(CallContext ctx)
    {
        return ctx.Window != null ? (long)ctx.Window.Id : 0;
    }

    /// <summary>Returns ids of all windows belonging to the app.</summary>
    public long[] GetWindowIds()
    {
        return _app.Windows.Select(w => (long)w.Id).ToArray();
    }

    private WryWindow? GetWindow(long windowId)
    {
        return _app.Windows.FirstOrDefault(w => (long)w.Id == windowId);
    }

    private WryWindow? ResolveWindow(CallContext ctx, long windowId)
    {
        if (windowId == CurrentWindow && ctx.Window != null)
            return ctx.Window;
        return GetWindow(windowId);
    }

    /// <summary>Get the window title. Use 0 or omit windowId for the calling window. Returns null if window not found.</summary>
    public string? GetTitle(CallContext ctx, long windowId = CurrentWindow)
    {
        return ResolveWindow(ctx, windowId)?.Title;
    }

    /// <summary>Set the window title. Omit windowId (or pass 0) for the calling window.</summary>
    public void SetTitle(CallContext ctx, string title, long windowId = CurrentWindow)
    {
        var w = ResolveWindow(ctx, windowId);
        if (w != null) w.Title = title;
    }

    /// <summary>Get the window size in logical pixels. Use 0 or omit windowId for the calling window. Returns null if window not found.</summary>
    public WindowSize? GetSize(CallContext ctx, long windowId = CurrentWindow)
    {
        var w = ResolveWindow(ctx, windowId);
        if (w == null) return null;
        var s = w.Size;
        return new WindowSize { Width = s.Width, Height = s.Height };
    }

    /// <summary>Set the window size in logical pixels. Omit windowId (or pass 0) for the calling window.</summary>
    public void SetSize(CallContext ctx, int width, int height, long windowId = CurrentWindow)
    {
        var w = ResolveWindow(ctx, windowId);
        if (w != null) w.Size = (width, height);
    }

    /// <summary>Get the window position in logical pixels. Use 0 or omit windowId for the calling window. Returns null if window not found.</summary>
    public WindowPosition? GetPosition(CallContext ctx, long windowId = CurrentWindow)
    {
        var w = ResolveWindow(ctx, windowId);
        if (w == null) return null;
        var p = w.Position;
        return new WindowPosition { X = p.X, Y = p.Y };
    }

    /// <summary>Set the window position in logical pixels. Omit windowId (or pass 0) for the calling window.</summary>
    public void SetPosition(CallContext ctx, int x, int y, long windowId = CurrentWindow)
    {
        var w = ResolveWindow(ctx, windowId);
        if (w != null) w.Position = (x, y);
    }

    /// <summary>Returns whether the window is visible. Use 0 or omit windowId for the calling window. Returns false if window not found.</summary>
    public bool GetVisible(CallContext ctx, long windowId = CurrentWindow)
    {
        return ResolveWindow(ctx, windowId)?.Visible ?? false;
    }

    /// <summary>Show or hide the window. Omit windowId (or pass 0) for the calling window.</summary>
    public void SetVisible(CallContext ctx, bool visible, long windowId = CurrentWindow)
    {
        var w = ResolveWindow(ctx, windowId);
        if (w != null) w.Visible = visible;
    }

    /// <summary>Returns whether the window is maximized. Use 0 or omit windowId for the calling window. Returns false if window not found.</summary>
    public bool GetMaximized(CallContext ctx, long windowId = CurrentWindow)
    {
        return ResolveWindow(ctx, windowId)?.Maximized ?? false;
    }

    /// <summary>Maximize or restore the window. Omit windowId (or pass 0) for the calling window.</summary>
    public void SetMaximized(CallContext ctx, bool maximized, long windowId = CurrentWindow)
    {
        var w = ResolveWindow(ctx, windowId);
        if (w != null) w.Maximized = maximized;
    }

    /// <summary>Returns whether the window is minimized. Use 0 or omit windowId for the calling window. Returns false if window not found.</summary>
    public bool GetMinimized(CallContext ctx, long windowId = CurrentWindow)
    {
        return ResolveWindow(ctx, windowId)?.Minimized ?? false;
    }

    /// <summary>Minimize or restore the window. Omit windowId (or pass 0) for the calling window.</summary>
    public void SetMinimized(CallContext ctx, bool minimized, long windowId = CurrentWindow)
    {
        var w = ResolveWindow(ctx, windowId);
        if (w != null) w.Minimized = minimized;
    }

    /// <summary>Request the window to close. Use 0 or omit windowId for the calling window.</summary>
    public void Close(CallContext ctx, long windowId = CurrentWindow)
    {
        ResolveWindow(ctx, windowId)?.Close();
    }

    /// <summary>Move keyboard focus to the window's webview. Use 0 or omit windowId for the calling window.</summary>
    public void Focus(CallContext ctx, long windowId = CurrentWindow)
    {
        ResolveWindow(ctx, windowId)?.FocusWebView();
    }

    /// <summary>Restore the window from minimized or maximized state. Use 0 or omit windowId for the calling window.</summary>
    public void Restore(CallContext ctx, long windowId = CurrentWindow)
    {
        ResolveWindow(ctx, windowId)?.Restore();
    }
}
