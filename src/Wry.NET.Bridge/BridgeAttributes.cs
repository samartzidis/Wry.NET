namespace Wry.NET.Bridge;

/// <summary>
/// Marks a class as a bridge service whose public methods
/// will be callable from the JavaScript frontend.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class BridgeServiceAttribute : Attribute
{
    /// <summary>
    /// Optional override for the service name used in JS bindings.
    /// Defaults to the class name.
    /// </summary>
    public string? Name { get; set; }
}

/// <summary>
/// Excludes a public method from the bridge. Methods marked with this
/// attribute will not be callable from JS and will not appear in
/// generated TypeScript bindings.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class BridgeIgnoreAttribute : Attribute { }

/// <summary>
/// Declares a typed event that can be emitted from .NET to JS.
/// Apply to a class or record that represents the event payload.
/// The generator will create typed subscription helpers in TypeScript.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class BridgeEventAttribute : Attribute
{
    /// <summary>
    /// The event name used on the wire. This is the string passed to
    /// <c>bridge.Emit()</c> and <c>events.on()</c>.
    /// </summary>
    public string Name { get; }

    public BridgeEventAttribute(string name)
    {
        Name = name;
    }
}
