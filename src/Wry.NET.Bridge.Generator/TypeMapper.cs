namespace Wry.NET.Bridge.Generator;

static class TypeMapper
{
    // No typeof(void) in C#; use constant for return type void
    private const string VoidFullName = "System.Void";

    internal static readonly HashSet<string> TaskTypeNames = new()
    {
        typeof(Task).FullName!,
        typeof(ValueTask).FullName!
    };

    internal static readonly HashSet<string> GenericTaskTypeNames = new()
    {
        typeof(Task<>).FullName!,
        typeof(ValueTask<>).FullName!
    };

    internal static string MapTypeToTS(Type type, Dictionary<string, TypeDef> models)
    {
        // Handle void
        if (type.FullName == VoidFullName)
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
        if (type.IsGenericType && type.GetGenericTypeDefinition().FullName == typeof(Nullable<>).FullName)
        {
            var inner = type.GetGenericArguments()[0];
            return $"{MapTypeToTS(inner, models)} | null";
        }

        // Primitives (type-safe full names)
        var mapped = type.FullName switch
        {
            _ when type.FullName == typeof(string).FullName => "string",
            _ when type.FullName == typeof(bool).FullName => "boolean",
            _ when type.FullName == typeof(byte).FullName || type.FullName == typeof(sbyte).FullName => "number",
            _ when type.FullName == typeof(short).FullName || type.FullName == typeof(ushort).FullName => "number",
            _ when type.FullName == typeof(int).FullName || type.FullName == typeof(uint).FullName => "number",
            _ when type.FullName == typeof(long).FullName || type.FullName == typeof(ulong).FullName => "number",
            _ when type.FullName == typeof(float).FullName || type.FullName == typeof(double).FullName || type.FullName == typeof(decimal).FullName => "number",
            _ when type.FullName == typeof(DateTime).FullName || type.FullName == typeof(DateTimeOffset).FullName => "string",
            _ when type.FullName == typeof(Guid).FullName => "string",
            _ when type.FullName == typeof(object).FullName => "unknown",
            _ => null
        };
        if (mapped != null) return mapped;

        // byte[] is special: System.Text.Json serializes it as a base64 string
        if (type.IsArray && type.GetElementType()?.FullName == typeof(byte).FullName)
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

            if (genDef is {} listLike && (
                listLike == typeof(List<>).FullName ||
                listLike == typeof(IList<>).FullName ||
                listLike == typeof(IEnumerable<>).FullName ||
                listLike == typeof(ICollection<>).FullName ||
                listLike == typeof(IReadOnlyList<>).FullName ||
                listLike == typeof(IReadOnlyCollection<>).FullName))
            {
                return $"{MapTypeToTS(genArgs[0], models)}[]";
            }

            // Dictionary<K,V>
            if (genDef is {} dictLike && (
                dictLike == typeof(Dictionary<,>).FullName ||
                dictLike == typeof(IDictionary<,>).FullName ||
                dictLike == typeof(IReadOnlyDictionary<,>).FullName))
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
