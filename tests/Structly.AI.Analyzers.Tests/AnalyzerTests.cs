using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Structly.AI.Analyzers;
using System.Collections.Immutable;
using System.Reflection;

namespace Structly.AI.Analyzers.Tests;

public sealed class AnalyzerTests
{
    static readonly ImmutableArray<MetadataReference> _references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
        .Split(Path.PathSeparator).Append(typeof(StructuredTask).Assembly.Location).Distinct()
        .Select(x => MetadataReference.CreateFromFile(x)).ToImmutableArray<MetadataReference>();

    [Theory]
    [InlineData("public class Dto { public string Text { get; init; } = \"\"; }", null)]
    [InlineData("public record Dto(string Text, int Count);", null)]
    [InlineData("[JsonConverter(typeof(JsonStringEnumConverter))] public enum Choice { A, B } public class Dto { public Choice? Value { get; set; } }", null)]
    [InlineData("[JsonConverter(typeof(JsonStringEnumConverter<Choice>))] public enum Choice { A, B } public class Dto { public Choice? Value { get; set; } }", null)]
    [InlineData("public enum Choice { A, B } public class Dto { [JsonConverter(typeof(JsonStringEnumConverter<Choice>))] public Choice? Value { get; set; } }", null)]

    [InlineData("public struct Dto { public int Count { get; set; } }", null)]
    [InlineData("public class Dto { public DateOnly Date { get; set; } public Guid Id { get; set; } }", null)]
    [InlineData("public class Dto { public List<string?>? Names { get; set; } public HashSet<int> Values { get; set; } = []; }", null)]
    [InlineData("public class Dto { [DynamicVocabulary(\"names\")] public IReadOnlyList<string?> Names { get; set; } = []; }", null)]
    [InlineData("public class Dto { [JsonIgnore] public object Bad { get; set; } = new(); [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public string Good { get; set; } = \"\"; }", null)]
    [InlineData("public class Dto { public string Text { get; } public Dto(string text) { Text = text; } }", null)]
    [InlineData("public class Dto { [JsonConstructor] Dto(string text) { Text = text; } public string Text { get; } }", null)]
    [InlineData("public class Base { [JsonInclude] int Hidden; } public class Dto : Base { public int Value { get; set; } }", null)]
    [InlineData("public class Base { public virtual string URLValue { get; set; } = \"\"; } public class Dto : Base { public override string URLValue { get; set; } = \"\"; }", null)]
    [InlineData("public enum Choice { [JsonStringEnumMemberName(\"a\")] A, B } public class Dto { public List<Choice?>? Choices { get; set; } }", null)]
    [InlineData("public class Dto { [StringConstraint(MinLength=1, MaxLength=10, Pattern=\"^[a-z]+$\")] public string Text { get; set; } = \"\"; [NumberConstraint(Minimum=0)] public int N { get; set; } [CollectionConstraint(MaxItems=4)] public int[] Items { get; set; } = []; }", null)]
    [InlineData("public class Dto { public Dictionary<string,string> Values { get; set; } = []; }", "Collection")]
    [InlineData("public class Dto { public byte[] Data { get; set; } = []; }", "Collection")]
    [InlineData("public class Dto { public int[,] Values { get; set; } = new int[1,1]; }", "Collection")]
    [InlineData("public class Dto { public System.Collections.ArrayList Values { get; set; } = []; }", "Collection")]
    [InlineData("public class Dto { public System.Collections.ObjectModel.ReadOnlyCollection<int> Values { get; set; } = new(new List<int>()); }", "CollectionConstruction")]
    [InlineData("public class Dto { public object Value { get; set; } = new(); }", "UnsupportedType")]
    [InlineData("public class Dto { public TimeSpan Value { get; set; } }", "UnsupportedType")]
    [InlineData("public class Dto { public (int, int) Value { get; set; } }", "UnsupportedType")]
    [InlineData("public class Dto { public System.Text.Json.JsonElement Value { get; set; } }", "UnsupportedType")]
    [InlineData("public interface Dto { string Text { get; set; } }", "UnsupportedType")]
    [InlineData("public abstract class Dto { public string Text { get; set; } = \"\"; }", "UnsupportedType")]
    [InlineData("public class Dto { public Dto? Child { get; set; } }", "Cycle")]
    [InlineData("public class Dto { }", "EmptyObject")]
    [InlineData("public class Dto { public int Value => 1; }", "ComputedProperty")]
    [InlineData("public class Dto { public int Value { get; private set; } }", "MemberAccess")]
    [InlineData("public class Dto { public int this[int i] { get => i; set {} } }", "Indexer")]
    [InlineData("public class Dto { public int Value { get; set; } public Dto(string other) {} }", "ConstructorBinding")]
    [InlineData("public class Dto { public int Value { get; set; } Dto() {} }", "Constructor")]
    [InlineData("public class Dto { [JsonPropertyName(\"\")] public int Value { get; set; } }", "MemberName")]
    [InlineData("public class Dto { [JsonPropertyName(\"x\")] public int A { get; set; } [JsonPropertyName(\"x\")] public int B { get; set; } }", "MemberName")]
    [InlineData("public class Dto { public int URLValue { get; set; } [JsonPropertyName(\"urlValue\")] public int Other { get; set; } }", "MemberName")]
    [InlineData("public class Dto { [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public string? Text { get; set; } }", "ConditionalIgnore")]
    [InlineData("public class Dto { [JsonInclude] public int Value; }", "FieldInclusion")]
    [InlineData("public class Dto { [JsonInclude] public int Value { get; set; } }", "SerializationOverride")]
    [InlineData("[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] public class Dto { public int Value { get; set; } }", "SerializationOverride")]
    [InlineData("[Schema(Name=\"bad name\")] public class Dto { public int Value { get; set; } }", "SchemaName")]
    [InlineData("[Description(\" \" )] public class Dto { public int Value { get; set; } }", "Description")]
    [InlineData("public class Dto { [Description(\"\")] public int Value { get; set; } }", "Description")]
    [InlineData("[Flags] public enum Choice { A=1, B=2 } public class Dto { public Choice Value { get; set; } }", "EnumFlags")]
    [InlineData("public enum Choice { A=1, B=1 } public class Dto { public Choice Value { get; set; } }", "EnumValues")]
    [InlineData("public enum Choice { [JsonStringEnumMemberName(\"\")] A } public class Dto { public Choice Value { get; set; } }", "EnumNames")]
    [InlineData("public class Dto { [DynamicVocabulary(\"x\")] public int Value { get; set; } }", "VocabularyTarget")]
    [InlineData("public class Dto { [DynamicVocabulary(\"\")] public string Value { get; set; } = \"\"; }", "VocabularyTarget")]
    [InlineData("public class Dto { [StringConstraint] public int Value { get; set; } }", "ConstraintTarget")]
    [InlineData("public class Dto { [CollectionConstraint] public string Value { get; set; } = \"\"; }", "ConstraintTarget")]
    [InlineData("public class Dto { [NumberConstraint] public string Value { get; set; } = \"\"; }", "ConstraintTarget")]
    [InlineData("public class Dto { [StringConstraint(MinLength=2, MaxLength=1)] public string Value { get; set; } = \"\"; }", "Bounds")]
    [InlineData("public class Dto { [CollectionConstraint(MinItems=-2)] public int[] Value { get; set; } = []; }", "Bounds")]
    [InlineData("public class Dto { [NumberConstraint(Minimum=double.PositiveInfinity)] public int Value { get; set; } }", "NumberBounds")]
    [InlineData("public class Dto { [StringConstraint(Format=\"unknown\")] public string Value { get; set; } = \"\"; }", "Format")]
    [InlineData("public class Dto { [StringConstraint(Pattern=\"(a)\")] public string Value { get; set; } = \"\"; }", "Pattern")]
    [InlineData("public class Dto { [StringConstraint(Pattern=\"[\")] public string Value { get; set; } = \"\"; }", "Pattern")]
    public async Task Static_contract_matches_runtime(string dto, string? code)
        => await AssertParity(dto, "Dto", code);

    [Theory]
    [InlineData("string")]
    [InlineData("int")]
    [InlineData("int[]")]
    [InlineData("DateOnly")]
    [InlineData("Dto?")]
    public async Task Root_matches_runtime(string type)
        => await AssertParity("public struct Dto { public int Value { get; set; } }", type, "Root");

    [Theory]
    [InlineData("byte[]", "Collection")]
    [InlineData("Dictionary<string,string>", "Collection")]
    [InlineData("object", "UnsupportedType")]
    public async Task Invalid_root_shape_has_the_runtime_issue_precedence(string type, string code)
        => await AssertParity("", type, code);

    [Theory]
    [InlineData(10, null)]
    [InlineData(11, "DepthLimit")]
    public async Task Object_depth_counts_only_objects_and_arrays(int levels, string? code)
    {
        var dto = String.Join("\n", Enumerable.Range(0, levels).Select(i =>
            $"public class D{i} {{ public {(i == levels - 1 ? "int" : $"D{i + 1}?")} Value {{ get; set; }} }}"));
        await AssertParity(dto, "D0", code);
    }

    [Theory]
    [InlineData(5000, null)]
    [InlineData(5001, "PropertyLimit")]
    public async Task Expanded_property_limit(int count, string? code)
        => await AssertParity("public class Dto { " + String.Join(" ", Enumerable.Range(0, count)
            .Select(i => $"public int P{i} {{ get; set; }}")) + " }", "Dto", code);

    [Theory]
    [InlineData(1000, null)]
    [InlineData(1001, "EnumLimit")]
    public async Task Enum_limit_is_1000(int count, string? code)
        => await AssertParity("public class Dto { public Choice Value { get; set; } } public enum Choice { "
            + String.Join(",", Enumerable.Range(0, count).Select(i => $"E{i}")) + " }", "Dto", code);

    [Fact]
    public async Task Enum_limit_counts_repeated_emissions()
        => await AssertParity("public class Dto { public Choice A { get; set; } public Choice B { get; set; } } public enum Choice { "
            + String.Join(",", Enumerable.Range(0, 501).Select(i => $"E{i}")) + " }", "Dto", "EnumLimit");

    [Fact]
    public async Task Large_enum_character_limit_matches_runtime()
        => await AssertParity("public class Dto { public Choice Value { get; set; } } public enum Choice { "
            + String.Join(",", Enumerable.Range(0, 251).Select(i => $"[JsonStringEnumMemberName(\"{new string('a', 60)}{i}\")] E{i}")) + " }", "Dto", "EnumStringLimit");

    [Fact]
    public async Task Schema_string_limit_counts_unicode_scalars()
    {
        await AssertParity($"public class Dto {{ [JsonPropertyName(\"{String.Concat(Enumerable.Repeat("😀", 30000))}{new string('a', 60000)}\")] public int Value {{ get; set; }} }}", "Dto", null);
        await AssertParity($"public class Dto {{ [JsonPropertyName(\"{new string('a', 120001)}\")] public int Value {{ get; set; }} }}", "Dto", "StringLimit");
    }

    [Fact]
    public async Task Unknown_naming_does_not_reject_a_valid_preserved_contract()
    {
        var compilation = Compile("public class Dto { public int URL { get; set; } public int url { get; set; } }",
            "public static void Run(StructuredTaskOptions options) => StructuredTask.Create<Dto>(options);");
        Assert.Empty(await Analyze(compilation));
        await AssertParity("public class Dto { public int URL { get; set; } public int url { get; set; } }", "Dto", null,
            "new() { Instructions = \"test\", SerializationProfile = new() { Naming = PropertyNaming.Preserve } }");
    }

    [Theory]
    [InlineData("CreateSchema")]
    [InlineData("CreateOutputSpecification", "new()")]
    [InlineData("ReadOutput", "\"{}\"")]
    public async Task Typed_task_operations_are_analyzed(string method, string arguments = "")
    {
        var compilation = Compile("public class Dto { public object Value { get; set; } = new(); }",
            $"public static void Run(StructuredTask<Dto> task) => task.{method}({arguments});");
        Assert.Contains("UnsupportedType", Assert.Single(await Analyze(compilation)).GetMessage());
    }

    [Fact]
    public async Task Generic_wrapper_and_unrelated_method_are_not_rejected()
    {
        var compilation = Compile("public class Dto { public object Value { get; set; } = new(); }",
            "public static void Run<T>() => StructuredTask.Create<T>(new() { Instructions = \"test\" }); public static T Create<T>() => default!; public static Dto Other() => Create<Dto>();");
        Assert.Empty(await Analyze(compilation));
    }

    [Theory]
    [InlineData("ExecuteAsync", "new() { Input = \"test\" }")]
    [InlineData("PrewarmAsync", "new() { Request = new() { Input = \"test\" } }")]
    public async Task Typed_provider_calls_are_analyzed(string method, string request)
    {
        var compilation = Compile("public class Dto { public object Value { get; set; } = new(); }",
            $"public static void Run(Structly.AI.OpenAI.OpenAiClient client, StructuredTask<Dto> task) => client.{method}(task, {request});");
        Assert.Contains("UnsupportedType", Assert.Single(await Analyze(compilation)).GetMessage());
    }

    [Fact]
    public async Task Generated_code_is_skipped()
    {
        var compilation = Compile("public class Dto { public object Value { get; set; } = new(); }",
            "public static void Run() => StructuredTask.Create<Dto>(new() { Instructions = \"test\" });");
        var generated = compilation.SyntaxTrees.Single().WithFilePath("Consumer.g.cs");
        Assert.Empty(await Analyze(compilation.RemoveAllSyntaxTrees().AddSyntaxTrees(generated)));
    }

    [Fact]
    public async Task Unresolved_qualified_legacy_attribute_still_has_guidance()
    {
        var compilation = CSharpCompilation.Create("Migration", [CSharpSyntaxTree.ParseText("[Legacy.StructuredLlmName(\"x\")] class Dto { }", cancellationToken: TestContext.Current.CancellationToken)],
            _references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var diagnostic = Assert.Single(await Analyze(compilation));
        Assert.Equal("STAI002", diagnostic.Id);
        Assert.Contains("JsonPropertyNameAttribute", diagnostic.GetMessage());
    }

    [Theory]
    [InlineData("SchemaName = \"bad name\"", "SchemaName")]
    [InlineData("Description = \" \"", "Description")]
    public async Task Constant_task_settings_are_checked(string setting, string code)
    {
        var compilation = Compile("public class Dto { public int Value { get; set; } }",
            $"public static void Run() => StructuredTask.Create<Dto>(new() {{ Instructions = \"test\", {setting} }});");
        Assert.Contains(code, Assert.Single(await Analyze(compilation)).GetMessage());
    }

    [Theory]
    [InlineData("StructuredLlmName", "JsonPropertyNameAttribute")]
    [InlineData("StructuredLlmStringLength", "StringConstraintAttribute")]
    [InlineData("StructuredLlmArrayLength", "CollectionConstraintAttribute")]
    [InlineData("StructuredLlmNumberRange", "NumberConstraintAttribute")]
    [InlineData("StructuredLlmEnumName", "JsonStringEnumMemberNameAttribute")]
    public async Task Legacy_attribute_guidance(string name, string replacement)
    {
        var compilation = Compile($"public class {name}Attribute : Attribute {{ }} public class Dto {{ [{name}] public int Value {{ get; set; }} }}", "public static void Run() { }");
        var diagnostic = Assert.Single(await Analyze(compilation));
        Assert.Equal("STAI002", diagnostic.Id);
        Assert.Contains(replacement, diagnostic.GetMessage());
    }

    static CSharpCompilation Compile(string dto, string method)
    {
        var source = """
            #nullable enable
            using System;
            using System.Collections.Generic;
            using System.ComponentModel;
            using System.Text.Json.Serialization;
            using Structly.AI;
            """ + dto + " public static class Entry { " + method + " }";
        var compilation = CSharpCompilation.Create("Consumer" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source)], _references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.Empty(compilation.GetDiagnostics().Where(x => x.Severity == DiagnosticSeverity.Error));
        return compilation;
    }

    static Task<ImmutableArray<Diagnostic>> Analyze(CSharpCompilation compilation)
        => compilation.WithAnalyzers([new StructuredSchemaAnalyzer()]).GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken);

    static async Task AssertParity(string dto, string type, string? code, string options = "new() { Instructions = \"test\" }")
    {
        var compilation = Compile(dto, $"public static object Run() => StructuredTask.Create<{type}>({options});");
        var diagnostics = await Analyze(compilation);
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(emit.Success, String.Join("\n", emit.Diagnostics));
        var assembly = Assembly.Load(stream.ToArray());
        StructuredIssue? runtime = null;
        try
        {
            assembly.GetType("Entry")!.GetMethod("Run")!.Invoke(null, null);
        }
        catch(TargetInvocationException exception) when(exception.InnerException is StructuredSchemaException schema)
        {
            runtime = Assert.Single(schema.Issues);
        }
        if(code is null)
        {
            Assert.Null(runtime);
            Assert.Empty(diagnostics);
        }
        else
        {
            Assert.NotNull(runtime);
            Assert.Equal(code, runtime.Code);
            var diagnostic = Assert.Single(diagnostics);
            Assert.Equal("STAI001", diagnostic.Id);
            Assert.StartsWith($"{runtime.Path}: {runtime.Code} —", diagnostic.GetMessage());
        }
    }
}
