using System.Text;

namespace Wry.NET.Bridge.Generator;

static class StringHelpers
{
    internal static string ToCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        if (char.IsLower(name[0])) return name;
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    internal static string FormatEnumValue(object? value)
    {
        if (value is string s) return $"\"{s}\"";
        return value?.ToString() ?? "0";
    }

    /// <summary>
    /// Convert a camelCase or snake_case event name to PascalCase for function naming.
    /// E.g. "progress" → "Progress", "task_completed" → "TaskCompleted"
    /// </summary>
    internal static string ToPascalCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;

        var sb = new StringBuilder();
        bool capitalizeNext = true;
        foreach (var ch in name)
        {
            if (ch == '_' || ch == '-')
            {
                capitalizeNext = true;
                continue;
            }
            sb.Append(capitalizeNext ? char.ToUpperInvariant(ch) : ch);
            capitalizeNext = false;
        }
        return sb.ToString();
    }

    /// <summary>
    /// Deletes any .ts files in the output directory that were not generated
    /// in this run. This removes stale bindings from renamed or deleted services.
    /// </summary>
    internal static void CleanupStaleFiles(string outputDir, HashSet<string> generatedFiles)
    {
        foreach (var file in Directory.GetFiles(outputDir, "*.ts"))
        {
            var fullPath = Path.GetFullPath(file);
            if (!generatedFiles.Contains(fullPath))
            {
                File.Delete(fullPath);
                Console.WriteLine($"[{CodeEmitter.ToolName}] Deleted stale file: {file}");
            }
        }
    }
}
