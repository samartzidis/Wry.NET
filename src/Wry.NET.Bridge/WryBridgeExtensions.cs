using Wry.NET;

namespace Wry.NET.Bridge;

/// <summary>
/// Fluent extension methods for registering built-in bridge services.
/// </summary>
public static class WryBridgeExtensions
{
    /// <summary>
    /// Registers all built-in bridge services: Dialog, Tray, and Window.
    /// Call after creating the WryApp and before <c>app.Run()</c>.
    /// </summary>
    /// <param name="bridge">The bridge.</param>
    /// <param name="app">The WryApp whose windows and tray icons are exposed.</param>
    /// <returns>The bridge for fluent chaining.</returns>
    public static WryBridge RegisterServices(this WryBridge bridge, WryApp app)
    {
        bridge.RegisterService(new Services.DialogService());
        bridge.RegisterService(new Services.TrayService(app, bridge));
        bridge.RegisterService(new Services.WindowService(app, bridge));
        return bridge;
    }
}
