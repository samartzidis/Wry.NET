namespace Wry.NET.Bridge.Generator.Tests.Fixtures;

/// <summary>
/// Local copies of bridge attributes. The generator matches by attribute Name
/// (not fully-qualified type), so these work identically to the real ones
/// in Wry.NET.Bridge.
/// </summary>

[AttributeUsage(AttributeTargets.Class)]
public class BridgeServiceAttribute : Attribute
{
    public string? Name { get; set; }
}

[AttributeUsage(AttributeTargets.Method)]
public class BridgeIgnoreAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Class)]
public class BridgeEventAttribute : Attribute
{
    public string Name { get; }
    public BridgeEventAttribute(string name) { Name = name; }
}
