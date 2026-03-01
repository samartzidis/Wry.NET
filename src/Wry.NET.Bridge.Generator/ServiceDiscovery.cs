using System.Reflection;

namespace Wry.NET.Bridge.Generator;

static class ServiceDiscovery
{
    internal static List<ServiceDef> DiscoverServices(Assembly assembly)
    {
        var services = new List<ServiceDef>();

        foreach (var type in assembly.GetExportedTypes())
        {
            var attr = type.CustomAttributes
                .FirstOrDefault(a => a.AttributeType.Name == "BridgeServiceAttribute");
            if (attr == null) continue;

            // Get optional Name property from the attribute
            var nameArg = attr.NamedArguments
                .FirstOrDefault(a => a.MemberName == "Name");
            var serviceName = nameArg.TypedValue.Value as string ?? type.Name;

            var methods = new List<MethodDef>();
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (method.IsSpecialName) 
                    continue;                    
                if (method.CustomAttributes.Any(a => a.AttributeType.Name == "BridgeIgnoreAttribute")) 
                    continue;

                var returnType = TypeMapper.UnwrapTaskType(method.ReturnType);
                var isAsync = TypeMapper.IsTaskType(method.ReturnType);

                // Filter out injected parameters — CallContext and CancellationToken are
                // auto-injected by the bridge and must not appear in the TS signature.
                var parameters = method.GetParameters()
                    .Where(p => p.ParameterType.Name != "CallContext"
                        && p.ParameterType.FullName != typeof(CancellationToken).FullName)
                    .Select(p => new ParamDef(p.Name ?? $"arg{p.Position}", p.ParameterType))
                    .ToList();

                methods.Add(new MethodDef(method.Name, parameters, returnType, isAsync));
            }

            services.Add(new ServiceDef(serviceName, methods));
            Console.WriteLine($"[{CodeEmitter.ToolName}] Found service: {serviceName} ({methods.Count} methods)");
        }

        return services;
    }

    /// <summary>
    /// Discover types marked with [BridgeEvent("eventName")].
    /// Each type represents an event payload with a named event.
    /// </summary>
    internal static List<EventDef> DiscoverEvents(Assembly assembly)
    {
        var events = new List<EventDef>();

        foreach (var type in assembly.GetExportedTypes())
        {
            var attr = type.CustomAttributes
                .FirstOrDefault(a => a.AttributeType.Name == "BridgeEventAttribute");
            if (attr == null) continue;

            // The event name is the first constructor argument
            var eventName = attr.ConstructorArguments.FirstOrDefault().Value as string;
            if (string.IsNullOrEmpty(eventName)) continue;

            events.Add(new EventDef(eventName, type));
            Console.WriteLine($"[{CodeEmitter.ToolName}] Found event: '{eventName}' → {type.Name}");
        }

        return events;
    }
}
