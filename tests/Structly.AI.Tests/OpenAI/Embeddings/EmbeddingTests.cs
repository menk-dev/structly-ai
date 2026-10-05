using System.Text.Json;
using System.Text.Json.Nodes;
using static Structly.AI.Tests.EmbeddingAndImageTestSupport;

namespace Structly.AI.Tests;

public sealed class EmbeddingTests
{
    [Fact]
    public async Task EmbeddingsSendFloatBatchAndReturnImmutableVectorsInInputOrder()
    {
        StructuredUsageEvent? observed = null;
        using var handler = new Handler(async message =>
        {
            Assert.EndsWith("/embeddings", message.RequestUri!.AbsoluteUri);
            Assert.Equal("request-key", message.Headers.Authorization!.Parameter);
            Assert.Equal("idem", Assert.Single(message.Headers.GetValues("Idempotency-Key")));
            using var document = JsonDocument.Parse(await message.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken));
            var root = document.RootElement;
            Assert.Equal("embedding", root.GetProperty("model").GetString());
            Assert.Equal("float", root.GetProperty("encoding_format").GetString());
            Assert.Equal(2, root.GetProperty("dimensions").GetInt32());
            Assert.Equal(new[] { "first", "second" }, root.GetProperty("input").EnumerateArray().Select(x => x.GetString()));
            Assert.False(root.TryGetProperty("store", out _));
            return Response(EmbeddingEnvelope());
        });
        using var http = new HttpClient(handler);
        var result = await Client(http).EmbedAsync(Embedding() with
        {
            CorrelationId = "correlation",
            IdempotencyKey = "idem",
            CaptureRawResponse = true,
            CredentialResolver = Credentials.FromStatic("request-key"),
            UsageObserver = (item, _) =>
            {
                observed = item;
                return ValueTask.CompletedTask;
            },
        }, TestContext.Current.CancellationToken);
        var vectors = result.EnsureSuccess();
        Assert.Equal(new float[] { 1, 2 }, vectors[0]);
        Assert.Equal(new float[] { 3, 4 }, vectors[1]);
        Assert.Throws<NotSupportedException>(() => ((IList<float>)vectors[0])[0] = 9);
        Assert.Throws<NotSupportedException>(() => ((IList<IReadOnlyList<float>>)vectors)[0] = new float[] { 9 });
        Assert.Equal("Embeddings", result.Metadata.Operation);
        Assert.Equal("correlation", result.Metadata.CorrelationId);
        Assert.Equal("embedding-resolved", result.Metadata.ResolvedModel);
        Assert.Equal("embedding", result.Metadata.RequestedModel);
        Assert.Equal(7, result.Metadata.Usage!.InputTokens);
        Assert.Equal(7, result.Metadata.Usage.TotalTokens);
        Assert.Null(result.Metadata.Usage.OutputTokens);
        Assert.NotNull(result.Metadata.RawResponse);
        Assert.True(observed!.Succeeded);
        Assert.Equal(result.Metadata.ExecutionId, observed.Metadata.ExecutionId);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("negative")]
    [InlineData("outside")]
    [InlineData("fractional")]
    [InlineData("missing")]
    [InlineData("empty-vector")]
    [InlineData("dimension-mismatch")]
    [InlineData("unequal-vectors")]
    [InlineData("string-component")]
    [InlineData("null-component")]
    [InlineData("infinite")]
    [InlineData("missing-model")]
    [InlineData("non-array")]
    public async Task InvalidEmbeddingOutputRetainsBilledUsage(string scenario)
    {
        var root = JsonNode.Parse(EmbeddingEnvelope())!;
        switch(scenario)
        {
            case "duplicate":
                root["data"]![1]!["index"] = 1;
                break;
            case "negative":
                root["data"]![0]!["index"] = -1;
                break;
            case "outside":
                root["data"]![0]!["index"] = 2;
                break;
            case "fractional":
                root["data"]![0]!["index"] = 0.5;
                break;
            case "missing":
                ((JsonArray)root["data"]!).RemoveAt(0);
                break;
            case "empty-vector":
                root["data"]![0]!["embedding"] = new JsonArray();
                break;
            case "dimension-mismatch":
            case "unequal-vectors":
                root["data"]![0]!["embedding"] = new JsonArray(1);
                break;
            case "string-component":
                root["data"]![0]!["embedding"]![0] = "secret";
                break;
            case "null-component":
                root["data"]![0]!["embedding"]![0] = null;
                break;
            case "infinite":
                root["data"]![0]!["embedding"]![0] = JsonNode.Parse("1e100");
                break;
            case "missing-model":
                ((JsonObject)root).Remove("model");
                break;
            case "non-array":
                root["data"] = new JsonObject();
                break;
        }

        using var handler = new Handler(_ => Task.FromResult(Response(root.ToJsonString())));
        using var http = new HttpClient(handler);
        StructuredUsageEvent? usage = null;
        var request = Embedding() with
        {
            Dimensions = scenario == "unequal-vectors" ? null : 2,
            UsageObserver = (item, _) =>
        {
            usage = item;
            return ValueTask.CompletedTask;
        },
        };
        var result = await Client(http).EmbedAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(StructuredErrorKind.InvalidResponse, result.Error!.Kind);
        Assert.Equal(7, result.Metadata.Usage!.InputTokens);
        Assert.Equal(StructuredErrorKind.InvalidResponse, usage!.FailureKind);
        Assert.DoesNotContain("secret", result.Error.Message);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("blank")]
    [InlineData("dimensions")]
    [InlineData("model")]
    [InlineData("reasoning")]
    [InlineData("timeout")]
    [InlineData("header")]
    public async Task InvalidEmbeddingInputNeverResolvesAuthOrSends(string scenario)
    {
        var auth = 0;
        var request = Embedding() with
        {
            CredentialResolver = _ =>
        {
            auth++;
            return ValueTask.FromResult<string?>("key");
        },
        };
        request = scenario switch
        {
            "empty" => request with { Inputs = [] },
            "blank" => request with { Inputs = [" "] },
            "dimensions" => request with { Dimensions = 0 },
            "model" => request with { ModelSelection = new() { ProfileName = "unknown" } },
            "reasoning" => request with { ModelSelection = new() { ProfileName = "balanced", ReasoningEffort = ReasoningEffort.High } },
            "timeout" => request with { TotalTimeout = TimeSpan.Zero },
            _ => request with { IdempotencyKey = "bad\r\nheader" },
        };
        using var handler = new Handler(_ => throw new InvalidOperationException("Should not send"));
        using var http = new HttpClient(handler);
        var result = await Client(http).EmbedAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(StructuredErrorKind.InvalidRequest, result.Error!.Kind);
        Assert.Equal(0, auth);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task DirectEmbeddingModelAndOmittedDimensionsAreSupported()
    {
        using var handler = new Handler(async message =>
        {
            using var document = JsonDocument.Parse(await message.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.Equal("direct", document.RootElement.GetProperty("model").GetString());
            Assert.False(document.RootElement.TryGetProperty("dimensions", out _));
            return Response(EmbeddingEnvelope());
        });
        using var http = new HttpClient(handler);
        Assert.True((await Client(http).EmbedAsync(Embedding() with { ModelSelection = new() { ModelId = "direct" }, Dimensions = null }, TestContext.Current.CancellationToken)).IsSuccess);
    }

}
