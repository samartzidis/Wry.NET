namespace Wry.Bridge.Generator;

/// <summary>
/// Reflection-based TypeScript binding generator for Wry Bridge services.
///
/// Loads a compiled .NET assembly, discovers types marked with [BridgeService],
/// and generates typed TypeScript modules that call the bridge runtime.
///
/// Usage: Wry.Bridge.Generator &lt;assembly-path&gt; &lt;output-dir&gt;
/// </summary>
class Program
{
    internal static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: BindingGenerator <assembly-path> <output-dir>");
            return 1;
        }

        var assemblyPath = Path.GetFullPath(args[0]);
        var outputDir = Path.GetFullPath(args[1]);

        if (!File.Exists(assemblyPath))
        {
            Console.Error.WriteLine($"Assembly not found: {assemblyPath}");
            return 1;
        }

        Console.WriteLine($"[BindingGenerator] Loading assembly: {assemblyPath}");
        Console.WriteLine($"[BindingGenerator] Output directory: {outputDir}");

        try
        {
            using var mlc = AssemblyLoader.CreateLoadContext(assemblyPath);
            var assembly = mlc.LoadFromAssemblyPath(assemblyPath);

            var services = ServiceDiscovery.DiscoverServices(assembly);
            var events = ServiceDiscovery.DiscoverEvents(assembly);

            if (services.Count == 0 && events.Count == 0)
            {
                Console.WriteLine("[BindingGenerator] No [BridgeService] or [BridgeEvent] types found.");
                return 0;
            }

            Directory.CreateDirectory(outputDir);

            // Track all files we generate so we can clean up stale ones
            var generatedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Collect all model types referenced by services
            var modelTypes = new Dictionary<string, TypeDef>();
            foreach (var svc in services)
            {
                foreach (var method in svc.Methods)
                {
                    ModelCollector.CollectModels(method.ReturnType, modelTypes, assembly);
                    foreach (var param in method.Parameters)
                        ModelCollector.CollectModels(param.Type, modelTypes, assembly);
                }
            }

            // Collect event payload types as models too
            foreach (var evt in events)
            {
                ModelCollector.CollectModels(evt.PayloadType, modelTypes, assembly);
            }

            // Generate service files
            foreach (var svc in services)
            {
                var code = CodeEmitter.GenerateServiceFile(svc, modelTypes);
                var filePath = Path.Combine(outputDir, $"{svc.Name}.ts");
                File.WriteAllText(filePath, code);
                generatedFiles.Add(Path.GetFullPath(filePath));
                Console.WriteLine($"[BindingGenerator] Generated {filePath}");
            }

            if (modelTypes.Count > 0)
            {
                var modelsCode = CodeEmitter.GenerateModelsFile(modelTypes);
                var modelsPath = Path.Combine(outputDir, "models.ts");
                File.WriteAllText(modelsPath, modelsCode);
                generatedFiles.Add(Path.GetFullPath(modelsPath));
                Console.WriteLine($"[BindingGenerator] Generated {modelsPath}");
            }

            // Generate typed event subscription helpers
            if (events.Count > 0)
            {
                var eventsCode = CodeEmitter.GenerateEventsFile(events, modelTypes);
                var eventsPath = Path.Combine(outputDir, "events.ts");
                File.WriteAllText(eventsPath, eventsCode);
                generatedFiles.Add(Path.GetFullPath(eventsPath));
                Console.WriteLine($"[BindingGenerator] Generated {eventsPath}");
            }

            // Generate index.ts barrel export
            var indexCode = CodeEmitter.GenerateIndexFile(services, events, modelTypes);
            var indexPath = Path.Combine(outputDir, "index.ts");
            File.WriteAllText(indexPath, indexCode);
            generatedFiles.Add(Path.GetFullPath(indexPath));
            Console.WriteLine($"[BindingGenerator] Generated {indexPath}");

            // Clean up stale .ts files from previous generations
            StringHelpers.CleanupStaleFiles(outputDir, generatedFiles);

            Console.WriteLine($"[BindingGenerator] Done. Generated bindings for {services.Count} service(s), {events.Count} event(s), {modelTypes.Count} model(s).");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[BindingGenerator] Error: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            return 1;
        }
    }
}
