using System.Text.Json.Serialization;

namespace Wry.Bridge.Generator.Tests.Fixtures;

// ==================== Services ====================

[BridgeService]
public class BasicService
{
    public string Greet(string name) => $"Hello, {name}!";
    public int Add(int a, int b) => a + b;
    public void DoNothing() { }
}

[BridgeService(Name = "CustomApi")]
public class CustomNamedService
{
    public string Echo(string msg) => msg;
}

[BridgeService]
public class AsyncService
{
    public Task<string> GetAsync(string name) => Task.FromResult(name);
    public ValueTask<int> GetValueAsync(int x) => new(x);
    public Task DoWorkAsync() => Task.CompletedTask;
}

[BridgeService]
public class IgnoredMethodService
{
    public string Visible() => "visible";

    [BridgeIgnore]
    public string Hidden() => "hidden";
}

[BridgeService]
public class CancellationService
{
    public Task<string> SlowMethod(int seconds, CancellationToken ct = default)
        => Task.FromResult("done");

    public Task<string> MultiParam(string name, int count, CancellationToken ct = default)
        => Task.FromResult(name);
}

[BridgeService]
public class ModelService
{
    public SimpleModel GetModel() => new();
    public DerivedModel GetDerived() => new();
    public NullableModel GetNullable() => new();
    public JsonCustomModel GetJsonCustom() => new();
    public List<SimpleModel> GetModels() => new();
    public Dictionary<string, int> GetDict() => new();
    public ModelWithEnum GetWithEnum() => new();
    public byte[] EchoBytes(byte[] data) => data;
}

/// <summary>Not marked with [BridgeService] — should be ignored.</summary>
public class NotAService
{
    public string NotExposed() => "";
}

// ==================== Models ====================

public class SimpleModel
{
    public string Name { get; set; } = "";
    public int Value { get; set; }
}

public class NullableModel
{
    public string Required { get; set; } = "";
    public string? Optional { get; set; }
    public int? NullableInt { get; set; }
}

public class JsonCustomModel
{
    [JsonPropertyName("custom_name")]
    public string CustomName { get; set; } = "";

    [JsonIgnore]
    public string Secret { get; set; } = "";

    public int Visible { get; set; }
}

public class BaseModel
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}

public class DerivedModel : BaseModel
{
    public string Extra { get; set; } = "";
}

public class ModelWithCollections
{
    public List<string> Tags { get; set; } = new();
    public Dictionary<string, int> Scores { get; set; } = new();
    public int[] Numbers { get; set; } = Array.Empty<int>();
}

public enum TestEnum
{
    None = 0,
    First = 1,
    Second = 2
}

public class ModelWithEnum
{
    public TestEnum Status { get; set; }
}

// ==================== Events ====================

[BridgeEvent("test_event")]
public class TestEvent
{
    public int Count { get; set; }
    public string Message { get; set; } = "";
}

[BridgeEvent("another")]
public class AnotherEvent
{
    public string Data { get; set; } = "";
}

/// <summary>Not marked with [BridgeEvent] — should be ignored.</summary>
public class NotAnEvent
{
    public string Whatever { get; set; } = "";
}
