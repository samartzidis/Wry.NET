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

    /// <summary>Minimum window size in pixels (width, height). Null = no minimum.</summary>
    public (int Width, int Height)? MinSize { get; set; }

    /// <summary>Maximum window size in pixels (width, height). Null = no maximum.</summary>
    public (int Width, int Height)? MaxSize { get; set; }

    /// <summary>Initial window position in pixels (x, y). Null = OS default.</summary>
    public (int X, int Y)? Position { get; set; }

    /// <summary>Whether the window is resizable. Default true.</summary>
    public bool Resizable { get; set; } = true;

    /// <summary>Whether the window starts in fullscreen mode. Default false.</summary>
    public bool Fullscreen { get; set; }

    /// <summary>Whether the window starts maximized. Default false.</summary>
    public bool Maximized { get; set; }

    /// <summary>Whether the window starts minimized. Default false.</summary>
    public bool Minimized { get; set; }

    /// <summary>Whether the window is always on top. Default false.</summary>
    public bool Topmost { get; set; }

    /// <summary>Whether the window is visible. Default true.</summary>
    public bool Visible { get; set; } = true;

    /// <summary>Enable developer tools. Default false.</summary>
    public bool Devtools { get; set; }

    /// <summary>Whether the window is transparent. Default false.</summary>
    public bool Transparent { get; set; }

    /// <summary>Whether the window has decorations (title bar, borders). Default true.</summary>
    public bool Decorations { get; set; } = true;

    /// <summary>Custom user agent string. Null = browser default.</summary>
    public string? UserAgent { get; set; }

    /// <summary>Initial zoom level. Default 1.0. Values &lt;= 0 are ignored.</summary>
    public double Zoom { get; set; } = 1.0;

    /// <summary>Enable back/forward navigation gestures. Default false. macOS only.</summary>
    public bool BackForwardGestures { get; set; }

    /// <summary>Enable media autoplay. Default false.</summary>
    public bool Autoplay { get; set; }

    /// <summary>Enable hotkey-based zoom (Ctrl+/Ctrl-). Default true.</summary>
    public bool HotkeysZoom { get; set; } = true;

    /// <summary>Enable clipboard access from JavaScript. Default false.</summary>
    public bool Clipboard { get; set; }

    /// <summary>Accept first mouse click when window is not focused. Default false. macOS only.</summary>
    public bool AcceptFirstMouse { get; set; }

    /// <summary>Use incognito/private browsing mode. Default false.</summary>
    public bool Incognito { get; set; }

    /// <summary>Whether the window receives focus when created. Default true.</summary>
    public bool Focused { get; set; } = true;

    /// <summary>Disable JavaScript execution. Default false.</summary>
    public bool JavascriptDisabled { get; set; }

    /// <summary>Background color as (R, G, B, A). Null = platform default.</summary>
    public (byte R, byte G, byte B, byte A)? BackgroundColor { get; set; }

    /// <summary>Background throttling policy. Null = default. Windows only.</summary>
    public int? BackgroundThrottling { get; set; }

    /// <summary>Window theme. 0 = system, 1 = light, 2 = dark. Default 0. Windows only.</summary>
    public int Theme { get; set; }

    /// <summary>Use HTTPS scheme for custom protocols. Default false. Windows only.</summary>
    public bool HttpsScheme { get; set; }

    /// <summary>Enable browser accelerator keys (F5, Ctrl+R, etc.). Default true. Windows only.</summary>
    public bool BrowserAcceleratorKeys { get; set; } = true;

    /// <summary>Scrollbar style. 0 = default, 1 = fluent overlay, 2 = none. Windows only.</summary>
    public int ScrollBarStyle { get; set; }

    /// <summary>Hide the window from the taskbar. Default false.</summary>
    public bool SkipTaskbar { get; set; }

    /// <summary>Prevent screen capture of the window content. Default false.</summary>
    public bool ContentProtected { get; set; }

    /// <summary>Enable window shadow. Default true.</summary>
    public bool Shadow { get; set; } = true;

    /// <summary>Keep the window below other windows. Default false.</summary>
    public bool AlwaysOnBottom { get; set; }

    /// <summary>Allow the window to be maximized. Default true.</summary>
    public bool Maximizable { get; set; } = true;

    /// <summary>Allow the window to be minimized. Default true.</summary>
    public bool Minimizable { get; set; } = true;

    /// <summary>Allow the window to be closed. Default true.</summary>
    public bool Closable { get; set; } = true;

    /// <summary>Allow the window to receive focus. Default true.</summary>
    public bool Focusable { get; set; } = true;

    /// <summary>Custom window class name. Null = default. Windows only.</summary>
    public string? WindowClassname { get; set; }

    /// <summary>
    /// Hooks invoked with the live window when it is materialized, before the user's onCreated callback.
    /// Extensions use this to auto-attach behavior at creation time.
    /// </summary>
    public List<Action<WryWindow>>? WindowCreatedActions { get; set; }
}
