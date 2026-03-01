namespace Wry.NET.Bridge.Generator;

static class TypeMapper
{
    internal static readonly HashSet<string> TaskTypeNames = new()
    {
        "System.Threading.Tasks.Task",
        "System.Threading.Tasks.ValueTask"
    };

    internal static readonly HashSet<string> GenericTaskTypeNames = new()
    {
        "System.Threading.Tasks.Task`1",
        "System.Threading.Tasks.ValueTask`1"
    };

    internal static string MapTypeToTS(Type type, Dictionary<string, TypeDef> models)
    {
        // Handle void
        if (type.FullName == "System.Void")
            return "void";

        // Handle Task / Task<T> / ValueTask / ValueTask<T>
        if (IsTaskType(type))
        {
            if (type.IsGenericType)
            {
                var inner = UnwrapTaskType(type);
                return MapTypeToTS(inner, models);
            }
            // Non-generic Task or ValueTask → void
            return "void";
        }

        // Handle Nullable<T>
        if (type.IsGenericType && type.GetGenericTypeDefinition().FullName == "System.Nullable`1")
        {
            var inner = type.GetGenericArguments()[0];
            return $"{MapTypeToTS(inner, models)} | null";
        }

        // Primitives
        var mapped = type.FullName switch
        {
            "System.String" => "string",
            "System.Boolean" => "boolean",
            "System.Byte" or "System.SByte" => "number",
            "System.Int16" or "System.UInt16" => "number",
            "System.Int32" or "System.UInt32" => "number",
            "System.Int64" or "System.UInt64" => "number",
            "System.Single" or "System.Double" or "System.Decimal" => "number",
            "System.DateTime" or "System.DateTimeOffset" => "string",
            "System.Guid" => "string",
            "System.Object" => "unknown",
            _ => null
        };
        if (mapped != null) return mapped;

        // byte[] is special: System.Text.Json serializes it as a base64 string
        if (type.IsArray && type.GetElementType()?.FullName == "System.Byte")
        {
            return "string";
        }

        // Arrays (non-byte)
        if (type.IsArray)
        {
            var elementType = type.GetElementType()!;
            return $"{MapTypeToTS(elementType, models)}[]";
        }

        // List<T>, IList<T>, IEnumerable<T>, ICollection<T>
        if (type.IsGenericType)
        {
            var genDef = type.GetGenericTypeDefinition().FullName;
            var genArgs = type.GetGenericArguments();

            if (genDef is "System.Collections.Generic.List`1"
                or "System.Collections.Generic.IList`1"
                or "System.Collections.Generic.IEnumerable`1"
                or "System.Collections.Generic.ICollection`1"
                or "System.Collections.Generic.IReadOnlyList`1"
                or "System.Collections.Generic.IReadOnlyCollection`1")
            {
                return $"{MapTypeToTS(genArgs[0], models)}[]";
            }

            // Dictionary<K,V>
            if (genDef is "System.Collections.Generic.Dictionary`2"
                or "System.Collections.Generic.IDictionary`2"
                or "System.Collections.Generic.IReadOnlyDictionary`2")
            {
                var keyTs = MapTypeToTS(genArgs[0], models);
                var valTs = MapTypeToTS(genArgs[1], models);
                return $"Record<{keyTs}, {valTs}>";
            }
        }

        // Enums
        if (type.IsEnum)
        {
            return type.Name;
        }

        // Known model type
        if (models.ContainsKey(type.FullName ?? type.Name))
        {
            return type.Name;
        }

        // Fallback
        return "unknown";
    }

    internal static bool IsTaskType(Type type)
    {
        if (type.FullName != null && TaskTypeNames.Contains(type.FullName)) return true;
        if (type.IsGenericType && type.GetGenericTypeDefinition().FullName is { } gn && GenericTaskTypeNames.Contains(gn)) return true;
        return false;
    }

    internal static Type UnwrapTaskType(Type type)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition().FullName is { } gn && GenericTaskTypeNames.Contains(gn))
            return type.GetGenericArguments()[0];

        // Task / ValueTask with no result => void-equivalent
        if (type.FullName != null && TaskTypeNames.Contains(type.FullName))
        {
            return type; // caller checks IsTaskType
        }

        return type;
    }
}
