using Wry.NET.Bridge.Generator;
using Wry.NET.Bridge.Generator.Tests.Helpers;
using Wry.NET.Bridge.Generator.Tests.Fixtures;

namespace Wry.NET.Bridge.Generator.Tests;

public class DiscoveryTests : IClassFixture<AssemblyHelper>
{
    private readonly AssemblyHelper _helper;

    public DiscoveryTests(AssemblyHelper helper)
    {
        _helper = helper;
    }

    #region DiscoverServices

    [Fact]
    public void DiscoverServices_FindsServicesWithAttribute()
    {
        var services = ServiceDiscovery.DiscoverServices(_helper.TestAssembly);

        Assert.Contains(services, s => s.Name == "BasicService");
        Assert.Contains(services, s => s.Name == "AsyncService");
        Assert.Contains(services, s => s.Name == "ModelService");
    }

    [Fact]
    public void DiscoverServices_SkipsClassWithoutAttribute()
    {
        var services = ServiceDiscovery.DiscoverServices(_helper.TestAssembly);

        Assert.DoesNotContain(services, s => s.Name == "NotAService");
    }

    [Fact]
    public void DiscoverServices_UsesCustomNameFromAttribute()
    {
        var services = ServiceDiscovery.DiscoverServices(_helper.TestAssembly);

        // CustomNamedService has [BridgeService(Name = "CustomApi")]
        Assert.Contains(services, s => s.Name == "CustomApi");
        Assert.DoesNotContain(services, s => s.Name == "CustomNamedService");
    }

    [Fact]
    public void DiscoverServices_ExcludesIgnoredMethods()
    {
        var services = ServiceDiscovery.DiscoverServices(_helper.TestAssembly);
        var svc = services.First(s => s.Name == "IgnoredMethodService");

        Assert.Contains(svc.Methods, m => m.Name == "Visible");
        Assert.DoesNotContain(svc.Methods, m => m.Name == "Hidden");
    }

    [Fact]
    public void DiscoverServices_FiltersCancellationTokenParameters()
    {
        var services = ServiceDiscovery.DiscoverServices(_helper.TestAssembly);
        var svc = services.First(s => s.Name == "CancellationService");

        var slowMethod = svc.Methods.First(m => m.Name == "SlowMethod");
        // Should have only 'seconds' param, CancellationToken stripped
        Assert.Single(slowMethod.Parameters);
        Assert.Equal("seconds", slowMethod.Parameters[0].Name);
    }

    [Fact]
    public void DiscoverServices_FiltersCancellationToken_KeepsOtherParams()
    {
        var services = ServiceDiscovery.DiscoverServices(_helper.TestAssembly);
        var svc = services.First(s => s.Name == "CancellationService");

        var multiParam = svc.Methods.First(m => m.Name == "MultiParam");
        // Should have 'name' and 'count', CancellationToken stripped
        Assert.Equal(2, multiParam.Parameters.Count);
        Assert.Equal("name", multiParam.Parameters[0].Name);
        Assert.Equal("count", multiParam.Parameters[1].Name);
    }

    [Fact]
    public void DiscoverServices_SkipsSpecialMethods()
    {
        var services = ServiceDiscovery.DiscoverServices(_helper.TestAssembly);
        var svc = services.First(s => s.Name == "BasicService");

        // Should not contain property getters/setters or compiler-generated methods
        Assert.All(svc.Methods, m => Assert.False(m.Name.StartsWith("get_") || m.Name.StartsWith("set_")));
    }

    [Fact]
    public void DiscoverServices_DetectsAsyncMethods()
    {
        var services = ServiceDiscovery.DiscoverServices(_helper.TestAssembly);
        var svc = services.First(s => s.Name == "AsyncService");

        var getAsync = svc.Methods.First(m => m.Name == "GetAsync");
        Assert.True(getAsync.IsAsync);

        var getValueAsync = svc.Methods.First(m => m.Name == "GetValueAsync");
        Assert.True(getValueAsync.IsAsync);
    }

    [Fact]
    public void DiscoverServices_BasicService_HasCorrectMethods()
    {
        var services = ServiceDiscovery.DiscoverServices(_helper.TestAssembly);
        var svc = services.First(s => s.Name == "BasicService");

        Assert.Equal(3, svc.Methods.Count);
        Assert.Contains(svc.Methods, m => m.Name == "Greet");
        Assert.Contains(svc.Methods, m => m.Name == "Add");
        Assert.Contains(svc.Methods, m => m.Name == "DoNothing");
    }

    #endregion

    #region DiscoverEvents

    [Fact]
    public void DiscoverEvents_FindsEventsWithAttribute()
    {
        var events = ServiceDiscovery.DiscoverEvents(_helper.TestAssembly);

        Assert.Contains(events, e => e.Name == "test_event");
        Assert.Contains(events, e => e.Name == "another");
    }

    [Fact]
    public void DiscoverEvents_SkipsClassWithoutAttribute()
    {
        var events = ServiceDiscovery.DiscoverEvents(_helper.TestAssembly);

        // NotAnEvent should not appear
        Assert.DoesNotContain(events, e =>
            e.PayloadType.Name == "NotAnEvent");
    }

    [Fact]
    public void DiscoverEvents_ReadsEventNameFromConstructorArg()
    {
        var events = ServiceDiscovery.DiscoverEvents(_helper.TestAssembly);

        var testEvent = events.First(e => e.Name == "test_event");
        Assert.Equal("TestEvent", testEvent.PayloadType.Name);

        var another = events.First(e => e.Name == "another");
        Assert.Equal("AnotherEvent", another.PayloadType.Name);
    }

    #endregion
}
