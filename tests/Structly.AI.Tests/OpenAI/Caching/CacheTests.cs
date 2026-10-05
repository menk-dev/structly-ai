using Structly.AI.OpenAI;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Structly.AI.Tests.ResponseTestSupport;

namespace Structly.AI.Tests;

public sealed class CacheTests
{
    [Theory]
    [InlineData("unknown")]
    [InlineData("legacy-modern")]
    [InlineData("modern-legacy")]
    [InlineData("mixed")]
    [InlineData("ttl")]
    [InlineData("mode")]
    [InlineData("no-marker")]
    [InlineData("five-explicit")]
    [InlineData("four-implicit")]
    [InlineData("blank-comparison")]
    [InlineData("blank-key")]
    [InlineData("blank-previous")]
    public async Task CacheInvalidCombinationsAreSuppressed(string scenario)
    {
        using var handler = new Handler(_ => throw new InvalidOperationException("Should not send"));
        using var http = new HttpClient(handler);
        var options = new OpenAiResponseOptions { Cache = new() { Mode = OpenAiCacheMode.Implicit } };
        var request = new OpenAiRequest { Input = "prompt", OpenAi = options };
        request = scenario switch
        {
            "unknown" => request with { ModelSelection = new() { ModelId = "unknown" } },
            "modern-legacy" => request with { OpenAi = new() { CacheRetention = OpenAiCacheRetention.Hours24 } },
            "mixed" => request with { OpenAi = options with { CacheRetention = OpenAiCacheRetention.Hours24 } },
            "ttl" => request with { OpenAi = new() { Cache = new() { Ttl = "1h" } } },
            "mode" => request with { OpenAi = new() { Cache = new() { Mode = (OpenAiCacheMode)100 } } },
            "no-marker" => request with { OpenAi = new() { Cache = new() { Mode = OpenAiCacheMode.Explicit } } },
            "blank-comparison" => request with { OpenAi = new() { Cache = new() { ComparisonResponseId = " " } } },
            "blank-key" => request with { OpenAi = new() { PromptCacheKey = " " } },
            "blank-previous" => request with { OpenAi = new() { PreviousResponseId = " " } },
            "five-explicit" or "four-implicit" => request with
            {
                Input = null,
                Messages = [new(MessageRole.User,
                Enumerable.Range(0, scenario == "five-explicit" ? 5 : 4).Select(_ => (ContentPart)new TextPart("stable") { CacheBreakpoint = true }).ToArray())],
                OpenAi = new() { Cache = new() { Mode = scenario == "five-explicit" ? OpenAiCacheMode.Explicit : OpenAiCacheMode.Implicit } },
            },
            _ => request,
        };
        var result = await Client(http, scenario != "legacy-modern").GenerateTextAsync(new() { Request = request }, TestContext.Current.CancellationToken);
        Assert.Equal(StructuredErrorKind.InvalidRequest, result.Error!.Kind);
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData(OpenAiCacheRetention.InMemory, "in_memory")]
    [InlineData(OpenAiCacheRetention.Hours24, "24h")]
    public async Task LegacyRetentionIsSentSeparately(OpenAiCacheRetention retention, string wire)
    {
        using var handler = new Handler(async message =>
        {
            using var doc = JsonDocument.Parse(await message.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.Equal(wire, doc.RootElement.GetProperty("prompt_cache_retention").GetString());
            Assert.False(doc.RootElement.TryGetProperty("prompt_cache_options", out _));
            return Response(Envelope());
        });
        using var http = new HttpClient(handler);
        Assert.True((await Client(http, false).GenerateTextAsync(new() { Request = new OpenAiRequest() { Input = "prompt", OpenAi = new() { CacheRetention = retention } } }, TestContext.Current.CancellationToken)).IsSuccess);
    }

    [Theory]
    [InlineData("bad-count")]
    [InlineData("bad-shape")]
    public async Task MalformedCacheDiagnosticsDoNotMaskText(string scenario)
    {
        var root = JsonNode.Parse(Envelope())!;
        root["prompt_cache_diagnostics"] = scenario == "bad-shape" ? JsonValue.Create(1) : new JsonObject { ["type"] = "new", ["cache_missed_tokens"] = "bad" };
        using var handler = new Handler(_ => Task.FromResult(Response(root.ToJsonString())));
        using var http = new HttpClient(handler);
        var result = await Client(http).GenerateTextAsync(new() { Request = new() { Input = "prompt" } }, TestContext.Current.CancellationToken);
        Assert.Equal("Hello", result.EnsureSuccess());
        Assert.Contains(result.Warnings, warning => warning.Code == "InvalidCacheDiagnostics");
    }

    [Theory]
    [InlineData(OpenAiCacheMode.Explicit, 4)]
    [InlineData(OpenAiCacheMode.Implicit, 3)]
    public async Task ValidCacheBoundariesPreserveEveryMarker(OpenAiCacheMode mode, int count)
    {
        using var handler = new Handler(async message =>
        {
            using var document = JsonDocument.Parse(await message.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken));
            var content = document.RootElement.GetProperty("input")[0].GetProperty("content");
            Assert.Equal(count, content.GetArrayLength());
            Assert.All(content.EnumerateArray(), part => Assert.Equal("explicit", part.GetProperty("prompt_cache_breakpoint").GetProperty("mode").GetString()));
            return Response(Envelope());
        });
        using var http = new HttpClient(handler);
        var result = await Client(http).GenerateTextAsync(new()
        {
            Request = new OpenAiRequest()
            {
                Messages = [new(MessageRole.User, Enumerable.Range(0, count).Select(_ => (ContentPart)new TextPart("stable") { CacheBreakpoint = true }).ToArray())],
                OpenAi = new() { Cache = new() { Mode = mode } },
            },
        }, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
    }

}
