using static Structly.AI.Tests.BatchTestSupport;

namespace Structly.AI.Tests;

public sealed class BatchPreparationTests
{
    [Fact]
    public void Response_batches_inherit_token_caps_but_not_transport_deadlines()
    {
        using var http = new HttpClient();
        var client = Client(http);
        var task = StructuredTask.Create<Answer>(new()
        {
            Instructions = "Extract",
            ExecutionDefaults = new()
            {
                MaxOutputTokens = 800,
                TotalTimeout = TimeSpan.FromSeconds(2),
                InactivityTimeout = TimeSpan.FromSeconds(1),
            },
        });
        var prepared = client.PrepareResponseBatch(task,
        [
            new("default", new() { Input = "input" }),
            new("override", new() { Input = "input", MaxOutputTokens = 1600 })
        ]);
        var caps = prepared.Jsonl.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(line)
                .GetProperty("body").GetProperty("max_output_tokens").GetInt32());
        Assert.Equal(new[] { 800, 1600 }, caps);
    }

    [Fact]
    public void PreparationRejectsDuplicateModelsAndTransportControls()
    {
        using var http = new HttpClient();
        var client = Client(http);
        Assert.Throws<ArgumentException>(() => client.PrepareEmbeddingBatch([Item("a"), Item("a")]));
        Assert.Throws<ArgumentException>(() => client.PrepareEmbeddingBatch([Item("a"), new("b", Item("b").Request with { ModelSelection = new() { ModelId = "other" } })]));
        Assert.Throws<ArgumentException>(() => client.PrepareEmbeddingBatch([new("a", Item("a").Request with { TotalTimeout = TimeSpan.FromSeconds(1) })]));
    }

}
