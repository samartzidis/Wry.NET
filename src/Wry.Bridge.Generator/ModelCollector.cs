using System.Reflection;

namespace Wry.Bridge.Generator;

static class ModelCollector
{
    internal static void CollectModels(Type type, Dictionary<string, TypeDef> models, Assembly sourceAssembly)
    {
        // Unwrap nullables, arrays, generics
        if (type.IsGenericType && type.GetGenericTypeDefinition().FullName == "System.Nullable`1")
        {
            CollectModels(type.GetGenericArguments()[0], models, sourceAssembly);
            return;
        }

        if (type.IsArray)
        {
            CollectModels(type.GetElementType()!, models, sourceAssembly);
            return;
        }

        if (type.IsGenericType)
        {
            foreach (var ga in type.GetGenericArguments())
                CollectModels(ga, models, sourceAssembly);
            return;
        }

        // Skip primitives and system types
        if (type.FullName == null) return;
        if (type.FullName.StartsWith("System.")) return;
        if (type.IsPrimitive) return;
        if (type.IsEnum)
        {
            // Collect enum
            if (!models.ContainsKey(type.FullName))
            {
                var enumValues = new List<EnumValueDef>();
                foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
                {
                    enumValues.Add(new EnumValueDef(field.Name, field.GetRawConstantValue()));
                }
                models[type.FullName] = new TypeDef(type.Name, type.FullName, TypeDefKind.Enum, null, enumValues);
            }
            return;
        }

        // Only collect types from the same assembly (user types)
        if (type.Assembly.FullName != sourceAssembly.FullName) return;

        if (models.ContainsKey(type.FullName)) return;

        // Detect base class (if it's a user type, not System.Object etc.)
        string? baseTypeName = null;
        var baseType = type.BaseType;
        if (baseType != null &&
            baseType.FullName != null &&
            !baseType.FullName.StartsWith("System.") &&
            !baseType.IsPrimitive &&
            baseType.Assembly.FullName == sourceAssembly.FullName)
        {
            baseTypeName = baseType.Name;
            // Ensure the base type is also collected as a model
            CollectModels(baseType, models, sourceAssembly);
        }

        // Collect only properties declared on this type (not inherited).
        // Base class properties are emitted via the `extends` clause in TS.
        var properties = new List<PropertyDef>();
        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            // Skip properties marked with [JsonIgnore]
            if (prop.CustomAttributes.Any(a => a.AttributeType.Name == "JsonIgnoreAttribute"))
                continue;

            var isNullableRef = IsNullableReferenceProperty(prop, type);
            var jsonName = GetJsonPropertyName(prop);
            properties.Add(new PropertyDef(prop.Name, prop.PropertyType, isNullableRef, jsonName));
        }

        models[type.FullName] = new TypeDef(type.Name, type.FullName, TypeDefKind.Interface, properties, null, baseTypeName);

        // Recurse into property types
        foreach (var prop in properties)
        {
            CollectModels(prop.Type, models, sourceAssembly);
        }
    }

    /// <summary>
    /// Detect NRT (Nullable Reference Type) annotations on a property.
    /// The compiler emits [Nullable(byte[])] or [NullableContext(byte)] attributes.
    /// A value of 2 means nullable, 1 means non-nullable, 0 means oblivious.
    /// </summary>
    internal static bool IsNullableReferenceProperty(PropertyInfo prop, Type declaringType)
    {
        // If the property type is a value type, NRT doesn't apply
        if (prop.PropertyType.IsValueType) return false;

        // Check for [Nullable] attribute on the property itself
        var nullableAttr = prop.CustomAttributes
            .FirstOrDefault(a => a.AttributeType.Name == "NullableAttribute" &&
                                  a.AttributeType.Namespace == "System.Runtime.CompilerServices");

        if (nullableAttr != null)
        {
            var ctorArg = nullableAttr.ConstructorArguments.FirstOrDefault();
            if (ctorArg.Value is byte b) return b == 2;
            if (ctorArg.Value is IReadOnlyCollection<CustomAttributeTypedArgument> bytes)
                return bytes.FirstOrDefault().Value is byte first && first == 2;
        }

        // Fall back to [NullableContext] on the declaring type
        var contextAttr = declaringType.CustomAttributes
            .FirstOrDefault(a => a.AttributeType.Name == "NullableContextAttribute" &&
                                  a.AttributeType.Namespace == "System.Runtime.CompilerServices");

        if (contextAttr != null)
        {
            var ctorArg = contextAttr.ConstructorArguments.FirstOrDefault();
            if (ctorArg.Value is byte b) return b == 2;
        }

        return false;
    }

    /// <summary>
    /// Read the [JsonPropertyName("name")] attribute value if present.
    /// Returns null if the attribute is not applied.
    /// </summary>
    internal static string? GetJsonPropertyName(PropertyInfo prop)
    {
        var attr = prop.CustomAttributes
            .FirstOrDefault(a => a.AttributeType.Name == "JsonPropertyNameAttribute");
        if (attr == null) return null;

        var ctorArg = attr.ConstructorArguments.FirstOrDefault();
        return ctorArg.Value as string;
    }
}
