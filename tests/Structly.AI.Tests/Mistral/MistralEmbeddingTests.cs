using Structly.AI.Embeddings;
using Structly.AI.Mistral;
using System.Text.Json;
using static Structly.AI.Tests.MistralFixture;

namespace Structly.AI.Tests;

public sealed class MistralEmbeddingTests
{
    [Fact]
    public async Task Embeddings_map_dimensions_reorder_vectors_and_observe_usage()
    {
        using var handler = new Handler(Vectors);
        using var http = new HttpClient(handler);
        StructuredUsageEvent? observed = null;
        var result = await new MistralClient(http, Options()).EmbedAsync(new()
        {
            Inputs = ["first", "second"],
            ModelSelection = new() { ModelId = "embed" },
            Dimensions = 2,
            CaptureRawResponse = true,
            UsageObserver = (usage, _) =>
            {
                observed = usage;
                return ValueTask.CompletedTask;
            },
        }, TestContext.Current.CancellationToken);
        Assert.Equal(new float[] { 1, 2 }, result.EnsureSuccess()[0]);
        Assert.Equal(new float[] { 3, 4 }, result.Value![1]);
        Assert.Throws<NotSupportedException>(() => ((IList<float>)result.Value[0])[0] = 99);
        Assert.Equal(3, observed!.Usage.InputTokens);
        Assert.Null(observed.Usage.OutputTokens);
        Assert.NotNull(result.Metadata.RawResponse);
        Assert.EndsWith("/embeddings", handler.Uris[0].AbsolutePath);
        using var document = JsonDocument.Parse(handler.Payloads[0]);
        Assert.Equal(2, document.RootElement.GetProperty("output_dimension").GetInt32());
        Assert.Equal("float", document.RootElement.GetProperty("output_dtype").GetString());
        Assert.False(document.RootElement.TryGetProperty("dimensions", out _));
    }

    [Theory]
    [InlineData("[{\"index\":0,\"embedding\":[1,2]},{\"index\":0,\"embedding\":[3,4]}]")]
    [InlineData("[{\"index\":0,\"embedding\":[1]},{\"index\":1,\"embedding\":[3,4]}]")]
    [InlineData("[{\"index\":0,\"embedding\":[1,2]},{\"index\":1,\"embedding\":[3,1e100]}]")]
    [InlineData("[]")]
    public async Task Invalid_vectors_retain_usage(string data)
    {
        using var handler = new Handler("{\"model\":\"embed\",\"usage\":{\"prompt_tokens\":3},\"data\":" + data + "}");
        using var http = new HttpClient(handler);
        StructuredUsageEvent? observed = null;
        var result = await new MistralClient(http, Options()).EmbedAsync(new()
        {
            Inputs = ["first", "second"],
            ModelSelection = new() { ModelId = "embed" },
            Dimensions = 2,
            UsageObserver = (usage, _) =>
            {
                observed = usage;
                return ValueTask.CompletedTask;
            },
        }, TestContext.Current.CancellationToken);
        Assert.Equal(StructuredErrorKind.InvalidResponse, result.Error!.Kind);
        Assert.Equal(3, result.Metadata.Usage!.InputTokens);
        Assert.False(observed!.Succeeded);
    }

    [Fact]
    public async Task Unsupported_embedding_controls_do_not_resolve_credentials()
    {
        using var http = new HttpClient();
        var options = Options();
        options.CredentialResolver = _ => throw new Exception("Must not resolve");
        var client = new MistralClient(http, options);
        var valid = new EmbeddingRequest { Inputs = ["input"], ModelSelection = new() { ModelId = "embed" } };
        foreach(var request in new[] { valid with { Inputs = [] }, valid with { Dimensions = 0 }, valid with { IdempotencyKey = "key" },
            valid with { ModelSelection = new() { ModelId = "embed", ReasoningEffort = ReasoningEffort.High } }, })
            Assert.Equal(StructuredErrorKind.InvalidRequest, (await client.EmbedAsync(request, TestContext.Current.CancellationToken)).Error!.Kind);
    }
}
