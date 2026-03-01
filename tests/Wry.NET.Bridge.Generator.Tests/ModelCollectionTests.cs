using System.Reflection;
using Wry.NET.Bridge.Generator;
using Wry.NET.Bridge.Generator.Tests.Helpers;
using Wry.NET.Bridge.Generator.Tests.Fixtures;

namespace Wry.NET.Bridge.Generator.Tests;

public class ModelCollectionTests : IClassFixture<AssemblyHelper>
{
    private readonly AssemblyHelper _helper;

    public ModelCollectionTests(AssemblyHelper helper)
    {
        _helper = helper;
    }

    private Dictionary<string, TypeDef> Collect(Type fixtureType)
    {
        var mlcType = _helper.LoadType(fixtureType);
        var models = new Dictionary<string, TypeDef>();
        ModelCollector.CollectModels(mlcType, models, _helper.TestAssembly);
        return models;
    }

    #region Basic Collection

    [Fact]
    public void CollectsSimpleModel_WithProperties()
    {
        var models = Collect(typeof(SimpleModel));

        Assert.Single(models);
        var model = models.Values.First();
        Assert.Equal("SimpleModel", model.Name);
        Assert.Equal(TypeDefKind.Interface, model.Kind);
        Assert.NotNull(model.Properties);
        Assert.Equal(2, model.Properties!.Count);
        Assert.Contains(model.Properties, p => p.Name == "Name");
        Assert.Contains(model.Properties, p => p.Name == "Value");
    }

    [Fact]
    public void SkipsSystemTypes()
    {
        // System.String should not be collected as a model
        var stringType = _helper.LoadCoreType("System.String");
        var models = new Dictionary<string, TypeDef>();
        ModelCollector.CollectModels(stringType, models, _helper.TestAssembly);

        Assert.Empty(models);
    }

    [Fact]
    public void SkipsPrimitiveTypes()
    {
        var intType = _helper.LoadCoreType("System.Int32");
        var models = new Dictionary<string, TypeDef>();
        ModelCollector.CollectModels(intType, models, _helper.TestAssembly);

        Assert.Empty(models);
    }

    #endregion

    #region Enums

    [Fact]
    public void CollectsEnum_WithValues()
    {
        var models = Collect(typeof(TestEnum));

        Assert.Single(models);
        var model = models.Values.First();
        Assert.Equal("TestEnum", model.Name);
        Assert.Equal(TypeDefKind.Enum, model.Kind);
        Assert.NotNull(model.EnumValues);
        Assert.Equal(3, model.EnumValues!.Count);
        Assert.Contains(model.EnumValues, v => v.Name == "None");
        Assert.Contains(model.EnumValues, v => v.Name == "First");
        Assert.Contains(model.EnumValues, v => v.Name == "Second");
    }

    #endregion

    #region Unwrapping

    [Fact]
    public void UnwrapsNullableValueType_AndRecurses()
    {
        // Nullable<SimpleModel> isn't valid, but Nullable<TestEnum> would be
        // Actually test with a type that has Nullable<T> property
        var models = Collect(typeof(NullableModel));

        Assert.Single(models); // Only NullableModel itself
        var model = models.Values.First();
        Assert.Equal("NullableModel", model.Name);
        // int? should not add int as a model
    }

    [Fact]
    public void UnwrapsArray_AndCollectsElementType()
    {
        // Create an array type of SimpleModel and collect
        var simpleType = _helper.LoadType(typeof(SimpleModel));
        var arrayType = simpleType.MakeArrayType();
        var models = new Dictionary<string, TypeDef>();
        ModelCollector.CollectModels(arrayType, models, _helper.TestAssembly);

        // Should have collected SimpleModel from the array element
        Assert.Contains(models.Values, m => m.Name == "SimpleModel");
    }

    [Fact]
    public void UnwrapsGenericCollection_AndCollectsElementType()
    {
        // ModelService.GetModels returns List<SimpleModel>
        // Test via the service discovery path
        var services = ServiceDiscovery.DiscoverServices(_helper.TestAssembly);
        var svc = services.First(s => s.Name == "ModelService");
        var getModels = svc.Methods.First(m => m.Name == "GetModels");

        var models = new Dictionary<string, TypeDef>();
        ModelCollector.CollectModels(getModels.ReturnType, models, _helper.TestAssembly);

        Assert.Contains(models.Values, m => m.Name == "SimpleModel");
    }

    #endregion

    #region Inheritance

    [Fact]
    public void DetectsBaseClass_AndSetsBaseTypeName()
    {
        var models = Collect(typeof(DerivedModel));

        // Should have both BaseModel and DerivedModel
        Assert.Equal(2, models.Count);

        var derived = models.Values.First(m => m.Name == "DerivedModel");
        Assert.Equal("BaseModel", derived.BaseTypeName);

        var baseModel = models.Values.First(m => m.Name == "BaseModel");
        Assert.Null(baseModel.BaseTypeName);
    }

    [Fact]
    public void UsesDeclaredOnlyProperties_NoDuplication()
    {
        var models = Collect(typeof(DerivedModel));

        var derived = models.Values.First(m => m.Name == "DerivedModel");
        var baseModel = models.Values.First(m => m.Name == "BaseModel");

        // DerivedModel should only have its own properties (Extra), not inherited (Id, Name)
        Assert.Single(derived.Properties!);
        Assert.Equal("Extra", derived.Properties![0].Name);

        // BaseModel should have Id and Name
        Assert.Equal(2, baseModel.Properties!.Count);
        Assert.Contains(baseModel.Properties, p => p.Name == "Id");
        Assert.Contains(baseModel.Properties, p => p.Name == "Name");
    }

    #endregion

    #region JSON Attributes

    [Fact]
    public void SkipsJsonIgnoreProperties()
    {
        var models = Collect(typeof(JsonCustomModel));
        var model = models.Values.First();

        // Secret property has [JsonIgnore], should be excluded
        Assert.DoesNotContain(model.Properties!, p => p.Name == "Secret");
    }

    [Fact]
    public void ReadsJsonPropertyName()
    {
        var models = Collect(typeof(JsonCustomModel));
        var model = models.Values.First();

        var customProp = model.Properties!.First(p => p.Name == "CustomName");
        Assert.Equal("custom_name", customProp.JsonName);
    }

    [Fact]
    public void RegularProperties_HaveNullJsonName()
    {
        var models = Collect(typeof(JsonCustomModel));
        var model = models.Values.First();

        var visibleProp = model.Properties!.First(p => p.Name == "Visible");
        Assert.Null(visibleProp.JsonName);
    }

    #endregion

    #region Nullable Reference Types

    [Fact]
    public void DetectsNullableReferenceType()
    {
        var models = Collect(typeof(NullableModel));
        var model = models.Values.First();

        var required = model.Properties!.First(p => p.Name == "Required");
        Assert.False(required.IsNullableRef);

        var optional = model.Properties!.First(p => p.Name == "Optional");
        Assert.True(optional.IsNullableRef);
    }

    [Fact]
    public void NullableValueType_IsNotMarkedAsNullableRef()
    {
        var models = Collect(typeof(NullableModel));
        var model = models.Values.First();

        // int? is Nullable<int>, not an NRT — IsNullableRef should be false
        var nullableInt = model.Properties!.First(p => p.Name == "NullableInt");
        Assert.False(nullableInt.IsNullableRef);
    }

    [Fact]
    public void DetectsNullableReferenceType_FromTypeNullableContext()
    {
        var models = Collect(typeof(TypeWithOptionalFromContext));
        var model = models.Values.First();

        var optional = model.Properties!.First(p => p.Name == "OptionalFromContext");
        Assert.True(optional.IsNullableRef);
    }

    #endregion

    #region Edge cases

    [Fact]
    public void CollectsEmptyModel_WithNoProperties()
    {
        var models = Collect(typeof(EmptyModel));

        Assert.Single(models);
        var model = models.Values.First();
        Assert.Equal("EmptyModel", model.Name);
        Assert.NotNull(model.Properties);
        Assert.Empty(model.Properties!);
    }

    [Fact]
    public void CollectsEnum_WithNegativeAndDuplicateValues()
    {
        var models = Collect(typeof(EdgeCaseEnum));

        Assert.Single(models);
        var model = models.Values.First();
        Assert.Equal("EdgeCaseEnum", model.Name);
        Assert.Equal(TypeDefKind.Enum, model.Kind);
        Assert.NotNull(model.EnumValues);
        Assert.Equal(3, model.EnumValues!.Count);
        Assert.Contains(model.EnumValues, v => v.Name == "Zero" && (int)(v.Value ?? 0) == 0);
        Assert.Contains(model.EnumValues, v => v.Name == "Negative" && (int)(v.Value ?? 0) == -1);
        Assert.Contains(model.EnumValues, v => v.Name == "SameAsZero" && (int)(v.Value ?? 0) == 0);
    }

    #endregion

    #region Deduplication

    [Fact]
    public void DoesNotDuplicateAlreadyCollectedModels()
    {
        var mlcType = _helper.LoadType(typeof(SimpleModel));
        var models = new Dictionary<string, TypeDef>();

        // Collect twice
        ModelCollector.CollectModels(mlcType, models, _helper.TestAssembly);
        ModelCollector.CollectModels(mlcType, models, _helper.TestAssembly);

        Assert.Single(models);
    }

    #endregion
}
