using System.Reflection;
using System.Security.Cryptography;

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

            // Discover from main assembly and project-referenced assemblies (e.g. Wry.NET, Wry.Bridge)
            var assembliesToScan = GetAssembliesToScan(assembly, assemblyPath, mlc);
            var services = new List<ServiceDef>();
            var events = new List<EventDef>();
            foreach (var asm in assembliesToScan)
            {
                services.AddRange(ServiceDiscovery.DiscoverServices(asm));
                events.AddRange(ServiceDiscovery.DiscoverEvents(asm));
            }

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

            var inputPaths = GetInputAssemblyPaths(assemblyPath, assembliesToScan);
            var inputHash = ComputeInputHash(inputPaths);
            var expectedFileNames = GetExpectedOutputFileNames(services, events, modelTypes);

            // Skip generation if all expected files exist and have matching hash in first line
            Directory.CreateDirectory(outputDir);
            var allUpToDate = true;
            foreach (var fileName in expectedFileNames)
            {
                var fullPath = Path.Combine(outputDir, fileName);
                var existingHash = TryReadHashFromFirstLine(fullPath);
                if (existingHash != inputHash)
                {
                    allUpToDate = false;
                    break;
                }
            }
            if (allUpToDate)
            {
                Console.WriteLine($"[BindingGenerator] Bindings up to date (input hash: {inputHash}). Skipping generation.");
                return 0;
            }

            var hashLine = "// " + inputHash + "\n";

            // Generate service files
            foreach (var svc in services)
            {
                var code = CodeEmitter.GenerateServiceFile(svc, modelTypes);
                var filePath = Path.Combine(outputDir, $"{svc.Name}.ts");
                File.WriteAllText(filePath, hashLine + code);
                generatedFiles.Add(Path.GetFullPath(filePath));
                Console.WriteLine($"[BindingGenerator] Generated {filePath}");
            }

            if (modelTypes.Count > 0)
            {
                var modelsCode = CodeEmitter.GenerateModelsFile(modelTypes);
                var modelsPath = Path.Combine(outputDir, "models.ts");
                File.WriteAllText(modelsPath, hashLine + modelsCode);
                generatedFiles.Add(Path.GetFullPath(modelsPath));
                Console.WriteLine($"[BindingGenerator] Generated {modelsPath}");
            }

            // Generate typed event subscription helpers
            if (events.Count > 0)
            {
                var eventsCode = CodeEmitter.GenerateEventsFile(events, modelTypes);
                var eventsPath = Path.Combine(outputDir, "events.ts");
                File.WriteAllText(eventsPath, hashLine + eventsCode);
                generatedFiles.Add(Path.GetFullPath(eventsPath));
                Console.WriteLine($"[BindingGenerator] Generated {eventsPath}");
            }

            // Generate index.ts barrel export
            var indexCode = CodeEmitter.GenerateIndexFile(services, events, modelTypes);
            var indexPath = Path.Combine(outputDir, "index.ts");
            File.WriteAllText(indexPath, hashLine + indexCode);
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

    /// <summary>
    /// Returns the main assembly plus any project-referenced assemblies that exist
    /// in the same directory (so we discover [BridgeService] / [BridgeEvent] from
    /// libraries like Wry.Bridge as well as the app).
    /// </summary>
    private static List<Assembly> GetAssembliesToScan(
        Assembly mainAssembly,
        string mainAssemblyPath,
        MetadataLoadContext mlc)
    {
        var result = new List<Assembly> { mainAssembly };
        var assemblyDir = Path.GetDirectoryName(mainAssemblyPath);
        if (string.IsNullOrEmpty(assemblyDir) || !Directory.Exists(assemblyDir))
            return result;

        var dllsInDir = new HashSet<string>(
            Directory.GetFiles(assemblyDir, "*.dll").Select(f => Path.GetFileName(f)!),
            StringComparer.OrdinalIgnoreCase);

        foreach (var refName in mainAssembly.GetReferencedAssemblies())
        {
            if (string.IsNullOrEmpty(refName.Name)) continue;
            var dllName = refName.Name + ".dll";
            if (!dllsInDir.Contains(dllName)) continue;

            try
            {
                var refAssembly = mlc.LoadFromAssemblyName(refName);
                result.Add(refAssembly);
            }
            catch
            {
                // Ignore load failures (e.g. version mismatch)
            }
        }

        return result;
    }

    /// <summary>
    /// Returns the full paths of the assemblies from GetAssembliesToScan (used for input hash computation).
    /// </summary>
    private static List<string> GetInputAssemblyPaths(string mainAssemblyPath, List<Assembly> assembliesToScan)
    {
        var result = new List<string>();
        for (var i = 0; i < assembliesToScan.Count; i++)
        {
            var path = i == 0 ? mainAssemblyPath : assembliesToScan[i].Location;
            if (!string.IsNullOrEmpty(path))
                result.Add(path);
        }
        return result;
    }

    /// <summary>
    /// Computes a combined SHA256 hash of the given assembly files (sorted by path for stability).
    /// </summary>
    private static string ComputeInputHash(List<string> assemblyPaths)
    {
        var sorted = assemblyPaths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
        using var combined = new MemoryStream();
        foreach (var path in sorted)
        {
            if (!File.Exists(path)) continue;
            var bytes = File.ReadAllBytes(path);
            var hash = SHA256.HashData(bytes);
            foreach (var b in hash)
                combined.WriteByte(b);
        }
        var finalHash = SHA256.HashData(combined.ToArray());
        return Convert.ToHexString(finalHash).ToLowerInvariant();
    }

    /// <summary>
    /// Returns the list of output file names (e.g. index.ts, Dialog.ts) that would be generated.
    /// </summary>
    private static List<string> GetExpectedOutputFileNames(
        List<ServiceDef> services,
        List<EventDef> events,
        Dictionary<string, TypeDef> modelTypes)
    {
        var names = new List<string> { "index.ts" };
        foreach (var svc in services)
            names.Add($"{svc.Name}.ts");
        if (modelTypes.Count > 0)
            names.Add("models.ts");
        if (events.Count > 0)
            names.Add("events.ts");
        return names;
    }

    /// <summary>
    /// Reads the first line of the file and extracts the hash (content after "// ").
    /// Returns null if the file doesn't exist or the line doesn't look like a hash comment.
    /// </summary>
    private static string? TryReadHashFromFirstLine(string filePath)
    {
        if (!File.Exists(filePath)) return null;
        var firstLine = File.ReadLines(filePath).FirstOrDefault();
        if (string.IsNullOrEmpty(firstLine) || !firstLine.StartsWith("// ", StringComparison.Ordinal))
            return null;
        return firstLine.AsSpan(3).Trim().ToString();
    }
}
