using System.Text.Json;

namespace Structly.AI.Tests;

public sealed class VocabularyTests
{
    static Dictionary<string, IReadOnlyList<string>> Values(params string[] values) => new() { ["choices"] = values };

    [Fact]
    public void VocabularyIsOrdinalSortedSnapshottedAndValidatesNullableItems()
    {
        var task = SchemaTests.Create<Choices>();
        var source = new List<string> { "z", "a", " A " };
        var values = new Dictionary<string, IReadOnlyList<string>> { ["choices"] = source, ["ignored"] = null! };
        var schema = task.CreateSchema(values);
        source.Clear();
        var properties = schema.GetProperty("properties");
        Assert.Equal([" A ", "a", "z"], properties.GetProperty("choice").GetProperty("enum").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal("null", properties.GetProperty("optional").GetProperty("anyOf")[1].GetProperty("type").GetString());
        Assert.Equal("null", properties.GetProperty("items").GetProperty("anyOf")[0].GetProperty("items").GetProperty("anyOf")[1].GetProperty("type").GetString());
        Assert.True(task.ReadOutput("""{"choice":"a","optional":null,"items":[null,"z"]}""", Values("a", "z")).IsSuccess);
        Assert.False(task.ReadOutput("""{"choice":"A","optional":null,"items":null}""", Values("a", "z")).IsSuccess);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("a")]
    public void InvalidReferencedValuesHavePredictableDiagnostics(string? second)
    {
        var task = SchemaTests.Create<Choices>();
        var exception = Assert.Throws<StructuredSchemaException>(() => task.CreateSchema(Values("a", second!)));
        Assert.Equal("VocabularyValue", exception.Issues[0].Code);
        Assert.Equal("$.choice", exception.Issues[0].Path);
    }

    [Fact]
    public void MissingEmptyVocabularyFailsLocallyAndUnreferencedSetsAreIgnored()
    {
        var task = SchemaTests.Create<Choices>();
        Assert.Equal("VocabularyMissing", Assert.Throws<StructuredSchemaException>(() => task.CreateSchema()).Issues[0].Code);
        Assert.Equal("VocabularyEmpty", Assert.Throws<StructuredSchemaException>(() => task.CreateSchema(Values())).Issues[0].Code);
        Assert.Equal(StructuredErrorKind.UnsupportedSchema, task.ReadOutput("{}", Values()).Error!.Kind);
        var plain = SchemaTests.Create<SchemaTests.Positional>();
        Assert.Equal(plain.CreateSchema().GetRawText(), plain.CreateSchema(Values("", "")).GetRawText());
    }

    [Fact]
    public async Task ReusedTaskKeepsConcurrentSchemasAndValidationIsolated()
    {
        var task = SchemaTests.Create<Choices>();
        await Task.WhenAll(Enumerable.Range(0, 40).Select(i => Task.Run(() =>
        {
            var value = "tenant-" + i;
            var values = Values(value);
            var schema = task.CreateSchema(values);
            Assert.Equal(value, schema.GetProperty("properties").GetProperty("choice").GetProperty("enum")[0].GetString());
            var json = JsonSerializer.Serialize(new { choice = value, optional = (string?)null, items = new[] { value } });
            Assert.True(task.ReadOutput(json, values).IsSuccess);
            Assert.False(task.ReadOutput(json, Values("other")).IsSuccess);
        })));
    }

    public sealed class Choices
    {
        [DynamicVocabulary("choices")]
        public string Choice { get; init; } = "";
        [DynamicVocabulary("choices")]
        public string? Optional { get; init; }
        [DynamicVocabulary("choices")]
        public List<string?>? Items { get; init; }
    }
}
