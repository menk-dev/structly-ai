using System.Text.Json;
using static Structly.AI.Tests.ResponseTestSupport;

namespace Structly.AI.Tests;

public sealed class OutputSpecificationTests
{
    [Fact]
    public void GuidanceUsesSerializedContractAndValidatesSample()
    {
        var task = Contract<Guided>();
        var guidance = task.CreateOutputSpecification(new(), Vocabulary("z", "a"));
        Assert.Contains(task.CreateSchema(Vocabulary("a", "z")).GetRawText(), guidance);
        Assert.Contains("- $.renamed: string", guidance);
        Assert.Contains("Explain this field", guidance);
        Assert.Contains("- $.items[]: string or null", guidance);
        var json = guidance.Split("Example of valid output JSON:\n")[1];
        var sample = task.ReadOutput(json, Vocabulary("a", "z")).EnsureSuccess();
        Assert.Equal("a", sample.Choice);
        Assert.Equal("xxx", sample.Label);
        Assert.Equal(2, sample.Items.Count);
        Assert.All(sample.Items, item => Assert.Null(item));
        Assert.InRange(sample.Number, -5, -2);
        Assert.Null(sample.Optional);
        Assert.Equal(guidance, task.CreateOutputSpecification(new(), Vocabulary("z", "a")));
    }

    [Fact]
    public void GuidanceSwitchesAndCallerExamplesAreEnforced()
    {
        var task = Contract<Patterned>();
        Assert.Throws<ArgumentException>(() => task.CreateOutputSpecification(new()));
        var example = "{\"code\":\"AB\"}";
        var guidance = task.CreateOutputSpecification(new() { IncludeFields = false, ExampleJson = example, AdditionalInstructions = "Use this style" });
        Assert.Equal("Example of valid output JSON:\n" + example + "\n\nUse this style", guidance);
        Assert.Throws<ArgumentException>(() => task.CreateOutputSpecification(new() { ExampleJson = "{\"code\":\"xx\"}" }));
        Assert.Throws<ArgumentException>(() => task.CreateOutputSpecification(new() { IncludeExample = false, ExampleJson = example }));
        Assert.Throws<ArgumentException>(() => task.CreateOutputSpecification(new() { AdditionalInstructions = " " }));
        Assert.Contains(task.CreateSchema().GetRawText(), task.CreateOutputSpecification(new() { IncludeExample = false }));
        Assert.Equal("", task.CreateOutputSpecification(new() { IncludeFields = false, IncludeExample = false }));
    }

    [Fact]
    public void GeneratedSamplesBoundExpandedCollectionWork()
    {
        var task = Contract<LargeSample>();
        Assert.Throws<ArgumentException>(() => task.CreateOutputSpecification(new()));
        Assert.Contains(task.CreateSchema().GetRawText(), task.CreateOutputSpecification(new() { IncludeExample = false }));
    }

    [Fact]
    public async Task ConcurrentGuidanceAndNullableEnumCollectionsKeepVocabulariesIndependent()
    {
        var bothSent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var handler = new Handler(async message =>
        {
            using var document = JsonDocument.Parse(await message.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken));
            var root = document.RootElement;
            var schema = root.GetProperty("text").GetProperty("format").GetProperty("schema");
            var choice = schema.GetProperty("properties").GetProperty("values").GetProperty("items").GetProperty("anyOf")[0].GetProperty("enum")[0].GetString();
            var guidance = root.GetProperty("input")[0].GetProperty("content")[1].GetProperty("text").GetString()!;
            Assert.Contains(JsonSerializer.Serialize(choice), guidance);
            Assert.DoesNotContain(JsonSerializer.Serialize(choice == "red" ? "blue" : "red"), guidance);
            if(Interlocked.Increment(ref calls) == 2)
                bothSent.SetResult();

            await bothSent.Task.WaitAsync(TestContext.Current.CancellationToken);
            return Response(Envelope(JsonSerializer.Serialize(new { values = new string?[] { null, choice }, enums = new string?[] { null, "yes" } })));
        });
        using var http = new HttpClient(handler);
        var client = Client(http);
        var task = Contract<NullableCollections>();
        var results = await Task.WhenAll(new[] { "red", "blue" }.Select(value => client.ExecuteAsync(task,
            new() { Input = "prompt", Vocabularies = Vocabulary(value), OutputSpecification = new() }, TestContext.Current.CancellationToken)));
        Assert.Equal("red", results[0].EnsureSuccess().Values[1]);
        Assert.Equal("blue", results[1].EnsureSuccess().Values[1]);
        Assert.All(results, result => Assert.Equal(Decision.Yes, result.EnsureSuccess().Enums[1]));
    }

}
