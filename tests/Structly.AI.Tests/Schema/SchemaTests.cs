using System.Collections;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Structly.AI.Tests;

public sealed class SchemaTests
{
    public static StructuredTask<T> Create<T>(PropertyNaming naming = PropertyNaming.CamelCase)
        => StructuredTask.Create<T>(new StructuredTaskOptions
        {
            Instructions = "Extract output.",
            SerializationProfile = new() { Naming = naming },
        });

    public static JsonElement NonNull(JsonElement element)
        => element.TryGetProperty("anyOf", out var branches) ? branches[0] : element;

    [Fact]
    public void PropertyOrderOverridesDeclarationOrderIncludingInheritedMembers()
    {
        var task = Create<OrderedChild>();
        string[] names = ["first", "reason", "matches", "baseReason", "baseAnswer", "last"];
        Assert.Equal(names, task.CreateSchema().GetProperty("required").EnumerateArray().Select(x => x.GetString()));
        using var serialized = JsonDocument.Parse(JsonSerializer.Serialize(new OrderedChild(), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        Assert.Equal(names, serialized.RootElement.EnumerateObject().Select(x => x.Name));
    }
    public class OrderedBase
    {
        public string BaseReason { get; init; } = "";
        public int BaseAnswer { get; init; }
        [JsonPropertyOrder(-1)]
        public int First { get; init; }
    }
    public sealed class OrderedChild : OrderedBase
    {
        public string Reason { get; init; } = "";
        public int Matches { get; init; }
        [JsonPropertyOrder(1)]
        public int Last { get; init; }
    }

    [Fact]
    public void EnumConvertersAndDeclarationOrderAreSupported()
    {
        var task = Create<CompatibilityPayload>();
        var schema = task.CreateSchema();
        string[] names = ["reason", "matches", "fit", "genericFit", "propertyFit", "genericPropertyFit"];
        Assert.Equal(names, schema.GetProperty("properties").EnumerateObject().Select(x => x.Name));
        Assert.Equal(names, schema.GetProperty("required").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(["Intended", "Good", "Stretch"], NonNull(schema.GetProperty("properties").GetProperty("fit")).GetProperty("enum").EnumerateArray().Select(x => x.GetString()));
        var guidance = task.CreateOutputSpecification(new() { IncludeExample = true });
        Assert.True(guidance.IndexOf("$.reason", StringComparison.Ordinal) < guidance.IndexOf("$.matches", StringComparison.Ordinal));
        var example = guidance.Split("Example of valid output JSON:\n")[1];
        using var document = JsonDocument.Parse(example);
        Assert.Equal(names, document.RootElement.EnumerateObject().Select(x => x.Name));
        Assert.True(task.ReadOutput("""{"reason":"why","matches":1,"fit":"Intended","genericFit":"Good","propertyFit":"Done","genericPropertyFit":"Done"}""").IsSuccess);
        Assert.True(task.ReadOutput("""{"reason":"why","matches":1,"fit":null,"genericFit":null,"propertyFit":null,"genericPropertyFit":null}""").IsSuccess);
        Assert.False(task.ReadOutput("""{"reason":"why","matches":1,"fit":0,"genericFit":null,"propertyFit":null,"genericPropertyFit":null}""").IsSuccess);
        Assert.True(Create<ConverterDto>().ReadOutput("""{"value":"Done"}""").IsSuccess);
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum TraitFit { Intended, Good, Stretch, }
    [JsonConverter(typeof(JsonStringEnumConverter<GenericFit>))]
    public enum GenericFit { Intended, Good, Stretch, }
    public sealed record CompatibilityPayload
    {
        public required string Reason { get; init; }
        public int Matches { get; init; }
        public TraitFit? Fit { get; init; }
        public GenericFit? GenericFit { get; init; }
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public State? PropertyFit { get; init; }
        [JsonConverter(typeof(JsonStringEnumConverter<State>))]
        public State? GenericPropertyFit { get; init; }
    }
    public sealed class InvalidConverterDto
    {
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public int Value { get; set; }
    }

    [Fact]
    public void SchemaIsClosedRequiredOrderedAndAlignedWithSerialization()
    {
        var task = Create<Ticket>();
        var schema = task.CreateSchema();
        Assert.Equal("ticket", task.SchemaName);
        Assert.Equal("A ticket", task.Description);
        Assert.Equal("object", schema.GetProperty("type").GetString());
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(["queue_name", "details", "nullableState", "state"], schema.GetProperty("required").EnumerateArray().Select(x => x.GetString()));
        var properties = schema.GetProperty("properties");
        Assert.Equal("Queue", properties.GetProperty("queue_name").GetProperty("description").GetString());
        Assert.Equal(new[] { "in-progress", "Done" }, properties.GetProperty("state").GetProperty("enum").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal("null", properties.GetProperty("nullableState").GetProperty("anyOf")[1].GetProperty("type").GetString());
        var output = task.ReadOutput("""{"queue_name":"billing","details":null,"nullableState":null,"state":"in-progress"}""");
        Assert.True(output.IsSuccess);
        Assert.Equal(State.Active, output.EnsureSuccess().State);
        var serialized = JsonSerializer.Serialize(output.Value, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
        });
        Assert.True(task.ReadOutput(serialized).IsSuccess);
        Assert.Equal(schema.GetRawText(), task.CreateSchema().GetRawText());
    }

    [Fact]
    public void NamingProfilesAndDetachedInspectionDoNotContaminateEachOther()
    {
        var camel = Create<Box<string>>();
        var preserve = Create<Box<string>>(PropertyNaming.Preserve);
        Assert.True(camel.CreateSchema().GetProperty("properties").TryGetProperty("value", out _));
        Assert.True(preserve.CreateSchema().GetProperty("properties").TryGetProperty("Value", out _));
        var node = JsonNode.Parse(camel.CreateSchema().GetRawText())!;
        node["properties"]!["value"]!["type"] = "boolean";
        Assert.Equal("string", NonNull(camel.CreateSchema().GetProperty("properties").GetProperty("value")).GetProperty("type").GetString());
        Assert.False(preserve.ReadOutput("""{"value":"x"}""").IsSuccess);
        Assert.True(preserve.ReadOutput("""{"Value":"x"}""").IsSuccess);
    }

    [Fact]
    public void NullabilityPropagatesThroughCollectionsEnumsAndWideningAnnotations()
    {
        var task = Create<NullableDto>();
        var properties = task.CreateSchema().GetProperty("properties");
        var lists = NonNull(properties.GetProperty("lists"));
        var inner = NonNull(lists.GetProperty("items"));
        var item = inner.GetProperty("items");
        Assert.Equal("null", item.GetProperty("anyOf")[1].GetProperty("type").GetString());
        Assert.True(properties.GetProperty("wide").TryGetProperty("anyOf", out _));
        Assert.True(properties.GetProperty("maybe").TryGetProperty("anyOf", out _));
        Assert.True(task.ReadOutput("""{"lists":[[null,"x"],null],"states":[null,"Done"],"wide":null,"maybe":null}""").IsSuccess);
        Assert.False(task.ReadOutput("""{"lists":[[null]],"states":["done"],"wide":null,"maybe":null}""").IsSuccess);
    }

    [Fact]
    public void SupportedConstructorsStructsInheritanceAndRepeatedDtosMaterialize()
    {
        Assert.Equal("x", Create<Positional>().ReadOutput("""{"name":"x","count":2}""").EnsureSuccess().Name);
        Assert.Equal(2, Create<StructDto>().ReadOutput("""{"count":2}""").EnsureSuccess().Count);
        Assert.Equal("x", Create<AttributedConstructor>().ReadOutput("""{"wire":"x"}""").EnsureSuccess().Value);
        Assert.True(Create<Inherited>().ReadOutput("""{"baseValue":1,"child":"x"}""").IsSuccess);
        Assert.True(Create<Repeated>().ReadOutput("""{"a":{"count":1},"b":{"count":2}}""").IsSuccess);
        Assert.True(Create<Override>().ReadOutput("""{"value":"x"}""").IsSuccess);
        Assert.True(Create<RequiredConstructor>().ReadOutput("""{"value":"x"}""").IsSuccess);
    }

    [Fact]
    public void StandardAndMaterializableCustomCollectionsAreAccepted()
    {
        Assert.True(Create<Box<int[]>>().ReadOutput("""{"value":[1,2]}""").IsSuccess);
        Assert.True(Create<Box<List<int>>>().ReadOutput("""{"value":[1,2]}""").IsSuccess);
        Assert.True(Create<Box<IReadOnlyList<int>>>().ReadOutput("""{"value":[1,2]}""").IsSuccess);
        Assert.True(Create<Box<IList<int>>>().ReadOutput("""{"value":[1,2]}""").IsSuccess);
        Assert.True(Create<Box<ICollection<int>>>().ReadOutput("""{"value":[1,2]}""").IsSuccess);
        Assert.True(Create<Box<IEnumerable<int>>>().ReadOutput("""{"value":[1,2]}""").IsSuccess);
        Assert.True(Create<Box<IReadOnlyCollection<int>>>().ReadOutput("""{"value":[1,2]}""").IsSuccess);
        Assert.True(Create<Box<HashSet<int>>>().ReadOutput("""{"value":[1,2]}""").IsSuccess);
        Assert.True(Create<Box<ISet<int>>>().ReadOutput("""{"value":[1,2]}""").IsSuccess);
        Assert.True(Create<Box<CustomList>>().ReadOutput("""{"value":[1,2]}""").IsSuccess);
        Assert.False(Create<Box<HashSet<int>>>().ReadOutput("""{"value":[1,1]}""").IsSuccess);
        Assert.False(Create<Box<ISet<State>>>().ReadOutput("""{"value":["Done","Done"]}""").IsSuccess);
    }

    [Theory]
    [MemberData(nameof(UnsupportedTypes))]
    public void UnsupportedShapesProduceActionableStartupIssues(Type type)
    {
        var method = typeof(SchemaTests).GetMethod(nameof(Create))!.MakeGenericMethod(type);
        var exception = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, [PropertyNaming.CamelCase]));
        var schemaException = Assert.IsType<StructuredSchemaException>(exception.InnerException);
        Assert.NotEmpty(schemaException.Issues);
        Assert.All(schemaException.Issues, issue =>
        {
            Assert.StartsWith("$", issue.Path);
            Assert.False(String.IsNullOrWhiteSpace(issue.Code));
            Assert.False(String.IsNullOrWhiteSpace(issue.Message));
        });
    }

    public static IEnumerable<object[]> UnsupportedTypes()
    {
        Type[] types = [typeof(int), typeof(string), typeof(int[]), typeof(object), typeof(IList<int>), typeof(Empty),
            typeof(Box<Dictionary<string, string>>), typeof(Box<IReadOnlyDictionary<string, string>>), typeof(Box<int[,]>),
            typeof(Box<byte[]>), typeof(Box<ArrayList>), typeof(Box<object>), typeof(Box<JsonElement>), typeof(Box<JsonObject>),
            typeof(Box<char>), typeof(Box<TimeSpan>), typeof(Box<TimeOnly>), typeof(Box<Uri>), typeof(Box<Action>),
            typeof(Box<(int, string)>), typeof(Box<Tuple<int, string>>), typeof(Box<IAsyncEnumerable<string>>),
            typeof(Box<AbstractDto>), typeof(Box<IOutput>), typeof(Cycle), typeof(CollectionCycle), typeof(Computed),
            typeof(PrivateSetter), typeof(AmbiguousConstructor), typeof(UnboundConstructor), typeof(Hidden), typeof(DuplicateName),
            typeof(BlankName), typeof(BlankDescription), typeof(ConditionalIgnore), typeof(DirectionIgnore), typeof(InvalidConverterDto),
            typeof(ExtensionDto), typeof(IncludedField), typeof(IncludedProperty), typeof(Polymorphic), typeof(NumberHandling),
            typeof(PopulateDto), typeof(Box<Flags>), typeof(Box<Aliases>), typeof(Box<EmptyEnum>), typeof(Box<BadWireEnum>),
            typeof(Box<DuplicateWireEnum>), typeof(Box<OnlyEnumerable>), typeof(BadStringTarget), typeof(BadNumberTarget),
            typeof(BadCollectionTarget), typeof(BadBounds), typeof(BadNumberBounds), typeof(BadVocabulary), typeof(BadPattern),
            typeof(BadFormat), typeof(IndexerDto), typeof(FieldsOnly)];
        foreach(var type in types)
            yield return [type];
    }

    [Fact]
    public void IgnoredMembersAreExcludedBeforeShapeAndAttributeChecks()
    {
        var task = Create<Ignored>();
        Assert.Equal(["kept"], task.CreateSchema().GetProperty("required").EnumerateArray().Select(x => x.GetString()));
        Assert.True(task.ReadOutput("""{"kept":1}""").IsSuccess);
    }

    [Fact]
    public void OptionsHaveLocalValidationAndMetadataPrecedence()
    {
        Assert.Throws<ArgumentNullException>(() => StructuredTask.Create<Ticket>(null!));
        Assert.Throws<ArgumentException>(() => StructuredTask.Create<Ticket>(new() { Instructions = " " }));
        Assert.Throws<ArgumentException>(() => StructuredTask.Create<Ticket>(new() { Instructions = "x", SchemaName = " " }));
        Assert.Throws<ArgumentException>(() => StructuredTask.Create<Ticket>(new() { Instructions = "x", SchemaName = new string('a', 65) }));
        Assert.Throws<ArgumentException>(() => StructuredTask.Create<Ticket>(new() { Instructions = "x", Description = " " }));
        Assert.Throws<ArgumentException>(() => Create<Ticket>((PropertyNaming)99));
        var task = StructuredTask.Create<Ticket>(new() { Instructions = "x", SchemaName = "overridden", Description = "Override" });
        Assert.Equal("overridden", task.SchemaName);
        Assert.Equal("Override", task.CreateSchema().GetProperty("description").GetString());
        Assert.Equal("Box_1", Create<Box<string>>().SchemaName);
    }

    [Schema(Name = "ticket", Description = "A ticket")]
    public sealed record Ticket
    {
        [JsonPropertyName("queue_name"), JsonPropertyOrder(-1), Description("Queue")]
        public required string Queue { get; init; }
        public string? Details { get; init; }
        public State? NullableState { get; init; }
        public State State { get; init; }
    }
    public enum State
    {
        [JsonStringEnumMemberName("in-progress")]
        Active, Done,
    }
    public sealed record Box<T>(T Value);
    public sealed record Positional(string Name, int Count);
    public sealed class RequiredConstructor(string value)
    {
        [JsonRequired]
        public string Value { get; } = value;
    }
    public struct StructDto { public int Count { get; set; } }
    public sealed class NullableDto
    {
        public List<List<string?>?>? Lists { get; init; }
        public List<State?> States { get; init; } = [];
        [AllowNull]
        public string Wide { get; set; } = "";
        [MaybeNull]
        public string Maybe { get; init; } = "";
    }
    public sealed class AttributedConstructor
    {
        public AttributedConstructor() { Value = "default"; }
        [JsonConstructor]
        public AttributedConstructor(string value) { Value = value; }
        [JsonPropertyName("wire")]
        public string Value { get; }
    }
    public class BaseDto { public int BaseValue { get; init; } }
    public sealed class Inherited : BaseDto { public string Child { get; init; } = ""; }
    public sealed record Repeated(StructDto A, StructDto B);
    public class VirtualBase { public virtual string Value { get; init; } = ""; }
    public sealed class Override : VirtualBase { public override string Value { get; init; } = ""; }
    public sealed class CustomList : List<int>;
    public sealed class Empty;
    public abstract class AbstractDto { public int Value { get; set; } }
    public interface IOutput { int Value { get; } }
    public sealed class Cycle { public Cycle? Next { get; set; } }
    public sealed class CollectionCycle : List<CollectionCycle>;
    public sealed class Computed { public int Value => 1; }
    public sealed class PrivateSetter { public int Value { get; private set; } }
    public sealed class AmbiguousConstructor
    {
        public AmbiguousConstructor(int value) { Value = value; }
        public AmbiguousConstructor(int value, string other) { Value = value; }
        public int Value { get; }
    }
    public sealed class UnboundConstructor(string other) { public int Value { get; set; } = other.Length; }
    public sealed class Hidden : BaseDto { public new int BaseValue { get; init; } }
    public sealed class DuplicateName
    {
        [JsonPropertyName("x")]
        public int A { get; set; }
        [JsonPropertyName("x")]
        public int B { get; set; }
    }
    public sealed class BlankName
    {
        [JsonPropertyName(" ")]
        public int A { get; set; }
    }
    [Description(" ")]
    public sealed class BlankDescription { public int A { get; set; } }
    public sealed class ConditionalIgnore
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Value { get; set; }
    }
    public sealed class DirectionIgnore
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenReading)]
        public string? Value { get; set; }
    }
    public sealed class ConverterDto
    {
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public State Value { get; set; }
    }
    public sealed class ExtensionDto
    {
        [JsonExtensionData]
        public Dictionary<string, JsonElement> Value { get; set; } = [];
    }
    public sealed class IncludedField
    {
        [JsonInclude]
        public int Value; public int Other { get; set; }
    }
    public sealed class IncludedProperty
    {
        [JsonInclude]
        public int Value { get; set; }
    }
    [JsonPolymorphic]
    public class Polymorphic { public int Value { get; set; } }
    public sealed class NumberHandling
    {
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public int Value { get; set; }
    }
    public sealed class PopulateDto
    {
        [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
        public List<int> Value { get; set; } = [];
    }
    [Flags]
    public enum Flags { A = 1, B = 2, }
    public enum Aliases { A = 1, B = 1 }
    public enum EmptyEnum { }
    public enum BadWireEnum
    {
        [JsonStringEnumMemberName(" ")]
        A,
    }
    public enum DuplicateWireEnum
    {
        [JsonStringEnumMemberName("x")]
        A,
        [JsonStringEnumMemberName("x")]
        B,
    }
    public sealed class OnlyEnumerable : IEnumerable<int> { public IEnumerator<int> GetEnumerator() => Enumerable.Empty<int>().GetEnumerator(); IEnumerator IEnumerable.GetEnumerator() => GetEnumerator(); }
    public sealed class BadStringTarget
    {
        [StringConstraint(MinLength = 1)]
        public int Value { get; set; }
    }
    public sealed class BadNumberTarget
    {
        [NumberConstraint(Minimum = 1)]
        public string Value { get; set; } = "";
    }
    public sealed class BadCollectionTarget
    {
        [CollectionConstraint(MinItems = 1)]
        public int Value { get; set; }
    }
    public sealed class BadBounds
    {
        [StringConstraint(MinLength = 2, MaxLength = 1)]
        public string Value { get; set; } = "";
    }
    public sealed class BadNumberBounds
    {
        [NumberConstraint(Minimum = Double.PositiveInfinity)]
        public double Value { get; set; }
    }
    public sealed class BadVocabulary
    {
        [DynamicVocabulary("x")]
        public int Value { get; set; }
    }
    public sealed class BadPattern
    {
        [StringConstraint(Pattern = "(?<=a)b")]
        public string Value { get; set; } = "";
    }
    public sealed class BadFormat
    {
        [StringConstraint(Format = "unknown")]
        public string Value { get; set; } = "";
    }
    public sealed class IndexerDto { public string this[int index] { get => "x"; set { } } }
    public sealed class FieldsOnly { public const int Value = 1; }
    public sealed class Ignored
    {
        [JsonIgnore, StringConstraint(MinLength = -99), JsonInclude]
        public Dictionary<string, object> Bad => [];
        [JsonIgnore(Condition = JsonIgnoreCondition.Never), JsonRequired]
        public int Kept { get; set; }
    }
}
