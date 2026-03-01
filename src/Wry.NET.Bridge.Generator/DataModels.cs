namespace Wry.NET.Bridge.Generator;

record ServiceDef(string Name, List<MethodDef> Methods);
record MethodDef(string Name, List<ParamDef> Parameters, Type ReturnType, bool IsAsync);
record ParamDef(string Name, Type Type);
record EventDef(string Name, Type PayloadType);

enum TypeDefKind { Interface, Enum }
record TypeDef(string Name, string FullName, TypeDefKind Kind, List<PropertyDef>? Properties, List<EnumValueDef>? EnumValues, string? BaseTypeName = null);
record PropertyDef(string Name, Type Type, bool IsNullableRef = false, string? JsonName = null);
record EnumValueDef(string Name, object? Value);
