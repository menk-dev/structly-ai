using System.ComponentModel;
using System.Reflection;
using System.Reflection.Emit;

namespace Structly.AI.Tests;

public sealed class SchemaLimitTests
{
    static StructuredTask<VocabularyDto> Task() => SchemaTests.Create<VocabularyDto>();
    static Dictionary<string, IReadOnlyList<string>> Values(int count, int width = 0)
        => new() { ["values"] = Enumerable.Range(0, count).Select(i => i.ToString("D4") + new string('x', width)).ToArray() };

    [Fact]
    public void EnumEntryLimitIncludesEveryEmittedOccurrence()
    {
        Task().CreateSchema(Values(1000));
        Assert.Equal("EnumLimit", Assert.Throws<StructuredSchemaException>(() => Task().CreateSchema(Values(1001))).Issues[0].Code);
        var repeated = SchemaTests.Create<RepeatedVocabulary>();
        repeated.CreateSchema(Values(500));
        Assert.Equal("EnumLimit", Assert.Throws<StructuredSchemaException>(() => repeated.CreateSchema(Values(501))).Issues[0].Code);
    }

    [Fact]
    public void LargeEnumCharacterCeilingStartsAbove250Entries()
    {
        Task().CreateSchema(Values(250, 60));
        Task().CreateSchema(Values(251, 55));
        Assert.Equal("EnumStringLimit", Assert.Throws<StructuredSchemaException>(() => Task().CreateSchema(Values(251, 56))).Issues[0].Code);
    }

    [Fact]
    public void RelevantCharacterBudgetIncludesNamesCountsScalarsAndExcludesDescriptions()
    {
        var task = Task();
        var values = new Dictionary<string, IReadOnlyList<string>> { ["values"] = [new string('x', 119995)] };
        task.CreateSchema(values); // 119995 enum characters + 5 characters in 'value'.
        values["values"] = [new string('x', 119996)];
        Assert.Equal("StringLimit", Assert.Throws<StructuredSchemaException>(() => task.CreateSchema(values)).Issues[0].Code);
        values["values"] = [String.Concat(Enumerable.Repeat("😀", 60000))];
        task.CreateSchema(values); // 60,000 Unicode scalars, not 120,000 UTF-16 code units.
        var described = StructuredTask.Create<VocabularyDto>(new() { Instructions = "x", Description = new string('d', 200000) });
        described.CreateSchema(Values(1));
    }

    [Fact]
    public void SerializedSchemaByteLimitAppliesEvenWhenRelevantStringsAreUnderTheirLimit()
    {
        var task = Task();
        var values = new Dictionary<string, IReadOnlyList<string>> { ["values"] = [String.Concat(Enumerable.Repeat("😀", 100000))] };
        Assert.Equal("SchemaSize", Assert.Throws<StructuredSchemaException>(() => task.CreateSchema(values)).Issues[0].Code);
        Assert.Equal("SchemaSize", Assert.Throws<StructuredSchemaException>(() => StructuredTask.Create<SchemaTests.StructDto>(new()
        {
            Instructions = "x",
            Description = new string('d', 1024 * 1024)
        })).Issues[0].Code);
    }

    [Fact]
    public void ObjectAndArrayDepthLimitIncludesTheRoot()
    {
        var type = typeof(int);
        for(var i = 0; i < 9; i++)
            type = type.MakeArrayType();
        CreateDynamic(typeof(SchemaTests.Box<>).MakeGenericType(type));
        type = type.MakeArrayType();
        Assert.Equal("DepthLimit", InvalidDynamic(typeof(SchemaTests.Box<>).MakeGenericType(type)).Issues[0].Code);
    }

    [Fact]
    public void PropertyLimitAccepts5000AndRejects5001WithoutExpandingTheWholeContract()
    {
        CreateDynamic(WideDto(5000));
        Assert.Equal("PropertyLimit", InvalidDynamic(WideDto(5001)).Issues[0].Code);
        var leaf = WideDto(70);
        // Each root property emits the same 70-property leaf again: 71 * 71 = 5041.
        Assert.Equal("PropertyLimit", InvalidDynamic(WideDto(71, leaf)).Issues[0].Code);
    }

    [Fact]
    public void ExcessivelyLargeOutputIsRejectedWithoutDeserialization()
    {
        var result = SchemaTests.Create<SchemaTests.Box<string>>().ReadOutput("{\"value\":\"" + new string('x', 16 * 1024 * 1024) + "\"}");
        Assert.Equal("OutputSize", result.Error!.Issues[0].Code);
    }

    static Type WideDto(int count, Type? propertyType = null)
    {
        propertyType ??= typeof(int);
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("SchemaLimit" + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.RunAndCollect);
        var type = assembly.DefineDynamicModule("Main").DefineType("Output", TypeAttributes.Public | TypeAttributes.Class);
        type.DefineDefaultConstructor(MethodAttributes.Public);
        for(var i = 0; i < count; i++)
        {
            var name = "p" + i;
            var field = type.DefineField("_" + name, propertyType, FieldAttributes.Private);
            var property = type.DefineProperty(name, PropertyAttributes.None, propertyType, null);
            var getter = type.DefineMethod("get_" + name, MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig, propertyType, Type.EmptyTypes);
            var il = getter.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, field);
            il.Emit(OpCodes.Ret);
            var setter = type.DefineMethod("set_" + name, MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig, typeof(void), [propertyType]);
            il = setter.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Stfld, field);
            il.Emit(OpCodes.Ret);
            property.SetGetMethod(getter);
            property.SetSetMethod(setter);
        }
        return type.CreateType()!;
    }

    static void CreateDynamic(Type type)
        => typeof(StructuredTask).GetMethod(nameof(StructuredTask.Create))!.MakeGenericMethod(type).Invoke(null, [new StructuredTaskOptions { Instructions = "x" }]);

    static StructuredSchemaException InvalidDynamic(Type type)
        => Assert.IsType<StructuredSchemaException>(Assert.Throws<TargetInvocationException>(() => CreateDynamic(type)).InnerException);

    public sealed class VocabularyDto { [DynamicVocabulary("values")] public string Value { get; set; } = ""; }
    public sealed class RepeatedVocabulary
    {
        [DynamicVocabulary("values")] public string A { get; set; } = "";
        [DynamicVocabulary("values")] public string B { get; set; } = "";
    }
}
