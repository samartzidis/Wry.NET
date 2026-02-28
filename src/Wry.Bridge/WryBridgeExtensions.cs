namespace Wry.Bridge;

/// <summary>
/// Fluent extension methods for registering built-in bridge services.
/// </summary>
public static class WryBridgeExtensions
{
    /// <summary>
    /// Registers the built-in Dialog service (message, ask, confirm, open, save).
    /// Callable from the frontend as <c>Dialog.Message</c>, <c>Dialog.Ask</c>, etc.
    /// </summary>
    /// <returns>The bridge for fluent chaining.</returns>
    public static WryBridge RegisterDialogService(this WryBridge bridge)
    {
        bridge.RegisterService(new Services.DialogService());
        return bridge;
    }
}
