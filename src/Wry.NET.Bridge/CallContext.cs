using Wry.NET;

namespace Wry.NET.Bridge;

/// <summary>
/// Call-scoped context passed to bridge service methods when declared as a parameter.
/// Use this to access the window that sent the current IPC message (e.g. for parenting dialogs or window-specific logic).
/// </summary>
/// <example>
/// <code>
/// public string GetTitle(CallContext ctx)
/// {
///     return ctx.Window?.Title ?? "";
/// }
/// </code>
/// </example>
public sealed class CallContext
{
    /// <summary>
    /// The window that sent the current bridge call, or null if unknown.
    /// </summary>
    public WryWindow? Window { get; }

    internal CallContext(WryWindow? window)
    {
        Window = window;
    }
}
