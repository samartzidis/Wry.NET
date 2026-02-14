using Wry.Bridge.Generator;

namespace Wry.Bridge.Generator.Tests;

public class TypeMappingTests
{
    private static string Map(Type type, Dictionary<string, TypeDef>? models = null)
        => TypeMapper.MapTypeToTS(type, models ?? new Dictionary<string, TypeDef>());

    #region Primitives

    [Fact]
    public void Maps_Void_To_Void()
        => Assert.Equal("void", Map(typeof(void)));

    [Fact]
    public void Maps_String_To_String()
        => Assert.Equal("string", Map(typeof(string)));

    [Fact]
    public void Maps_Boolean_To_Boolean()
        => Assert.Equal("boolean", Map(typeof(bool)));

    [Theory]
    [MemberData(nameof(NumericTypeData))]
    public void Maps_NumericTypes_To_Number(Type type)
        => Assert.Equal("number", Map(type));

    public static IEnumerable<object[]> NumericTypeData => new[]
    {
        new object[] { typeof(byte) },
        new object[] { typeof(sbyte) },
        new object[] { typeof(short) },
        new object[] { typeof(ushort) },
        new object[] { typeof(int) },
        new object[] { typeof(uint) },
        new object[] { typeof(long) },
        new object[] { typeof(ulong) },
        new object[] { typeof(float) },
        new object[] { typeof(double) },
        new object[] { typeof(decimal) },
    };

    [Fact]
    public void Maps_DateTime_To_String()
        => Assert.Equal("string", Map(typeof(DateTime)));

    [Fact]
    public void Maps_DateTimeOffset_To_String()
        => Assert.Equal("string", Map(typeof(DateTimeOffset)));

    [Fact]
    public void Maps_Guid_To_String()
        => Assert.Equal("string", Map(typeof(Guid)));

    [Fact]
    public void Maps_Object_To_Unknown()
        => Assert.Equal("unknown", Map(typeof(object)));

    #endregion

    #region Arrays

    [Fact]
    public void Maps_ByteArray_To_String()
        => Assert.Equal("string", Map(typeof(byte[])));

    [Fact]
    public void Maps_IntArray_To_NumberArray()
        => Assert.Equal("number[]", Map(typeof(int[])));

    [Fact]
    public void Maps_StringArray_To_StringArray()
        => Assert.Equal("string[]", Map(typeof(string[])));

    #endregion

    #region Nullable

    [Fact]
    public void Maps_NullableInt_To_NumberOrNull()
        => Assert.Equal("number | null", Map(typeof(int?)));

    [Fact]
    public void Maps_NullableBool_To_BooleanOrNull()
        => Assert.Equal("boolean | null", Map(typeof(bool?)));

    [Fact]
    public void Maps_NullableDateTime_To_StringOrNull()
        => Assert.Equal("string | null", Map(typeof(DateTime?)));

    #endregion

    #region Collections

    [Fact]
    public void Maps_ListOfString_To_StringArray()
        => Assert.Equal("string[]", Map(typeof(List<string>)));

    [Fact]
    public void Maps_ListOfInt_To_NumberArray()
        => Assert.Equal("number[]", Map(typeof(List<int>)));

    [Fact]
    public void Maps_IEnumerableOfString_To_StringArray()
        => Assert.Equal("string[]", Map(typeof(IEnumerable<string>)));

    [Fact]
    public void Maps_IReadOnlyListOfInt_To_NumberArray()
        => Assert.Equal("number[]", Map(typeof(IReadOnlyList<int>)));

    [Fact]
    public void Maps_DictionaryStringInt_To_Record()
        => Assert.Equal("Record<string, number>", Map(typeof(Dictionary<string, int>)));

    [Fact]
    public void Maps_IDictionaryStringString_To_Record()
        => Assert.Equal("Record<string, string>", Map(typeof(IDictionary<string, string>)));

    #endregion

    #region Async Unwrapping

    [Fact]
    public void Maps_TaskOfString_To_String()
        => Assert.Equal("string", Map(typeof(Task<string>)));

    [Fact]
    public void Maps_TaskOfInt_To_Number()
        => Assert.Equal("number", Map(typeof(Task<int>)));

    [Fact]
    public void Maps_ValueTaskOfString_To_String()
        => Assert.Equal("string", Map(typeof(ValueTask<string>)));

    [Fact]
    public void Maps_ValueTaskOfInt_To_Number()
        => Assert.Equal("number", Map(typeof(ValueTask<int>)));

    #endregion

    #region Model / Enum References

    [Fact]
    public void Maps_KnownModel_To_TypeName()
    {
        var models = new Dictionary<string, TypeDef>
        {
            ["Wry.Bridge.Generator.Tests.Fixtures.SimpleModel"] = new TypeDef(
                "SimpleModel", "Wry.Bridge.Generator.Tests.Fixtures.SimpleModel",
                TypeDefKind.Interface,
                new List<PropertyDef>(), null)
        };

        Assert.Equal("SimpleModel", Map(typeof(Fixtures.SimpleModel), models));
    }

    [Fact]
    public void Maps_Enum_To_TypeName()
    {
        Assert.Equal("TestEnum", Map(typeof(Fixtures.TestEnum)));
    }

    [Fact]
    public void Maps_UnknownReferenceType_To_Unknown()
    {
        // A type not in models and not a known system type
        Assert.Equal("unknown", Map(typeof(System.Text.StringBuilder)));
    }

    #endregion

    #region IsTaskType / UnwrapTaskType

    [Fact]
    public void IsTaskType_Task_ReturnsTrue()
        => Assert.True(TypeMapper.IsTaskType(typeof(Task)));

    [Fact]
    public void IsTaskType_TaskOfT_ReturnsTrue()
        => Assert.True(TypeMapper.IsTaskType(typeof(Task<string>)));

    [Fact]
    public void IsTaskType_ValueTask_ReturnsTrue()
        => Assert.True(TypeMapper.IsTaskType(typeof(ValueTask)));

    [Fact]
    public void IsTaskType_ValueTaskOfT_ReturnsTrue()
        => Assert.True(TypeMapper.IsTaskType(typeof(ValueTask<int>)));

    [Fact]
    public void IsTaskType_String_ReturnsFalse()
        => Assert.False(TypeMapper.IsTaskType(typeof(string)));

    [Fact]
    public void UnwrapTaskType_TaskOfString_ReturnsString()
        => Assert.Equal(typeof(string), TypeMapper.UnwrapTaskType(typeof(Task<string>)));

    [Fact]
    public void UnwrapTaskType_ValueTaskOfInt_ReturnsInt()
        => Assert.Equal(typeof(int), TypeMapper.UnwrapTaskType(typeof(ValueTask<int>)));

    [Fact]
    public void UnwrapTaskType_NonTask_ReturnsSameType()
        => Assert.Equal(typeof(string), TypeMapper.UnwrapTaskType(typeof(string)));

    #endregion
}
