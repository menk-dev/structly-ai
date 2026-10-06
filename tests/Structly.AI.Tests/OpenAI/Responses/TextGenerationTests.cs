using Structly.AI.OpenAI;
using System.Text.Json;
using static Structly.AI.Tests.ResponseTestSupport;

namespace Structly.AI.Tests;

public sealed class TextGenerationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TextHasNoSchemaAndPreservesContinuationCacheMetadataAndProgress(bool stream)
    {
        var progress = new List<StructuredProgressKind>();
        StructuredUsageEvent? usage = null;
        using var handler = new Handler(async message =>
        {
            using var document = JsonDocument.Parse(await message.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken));
            var root = document.RootElement;
            Assert.False(root.TryGetProperty("text", out _));
            Assert.Equal("new instructions", root.GetProperty("instructions").GetString());
            Assert.Equal("prior", root.GetProperty("previous_response_id").GetString());
            Assert.True(root.GetProperty("store").GetBoolean());
            Assert.Equal("low", root.GetProperty("reasoning").GetProperty("effort").GetString());
            Assert.Equal("compare", root.GetProperty("prompt_cache_options").GetProperty("comparison_response_id").GetString());
            Assert.Equal("account", root.GetProperty("prompt_cache_key").GetString());
            return Response(Envelope(), stream);
        });
        using var http = new HttpClient(handler);
        var result = await Client(http).GenerateTextAsync(new()
        {
            Instructions = "new instructions",
            Request = new OpenAiRequest()
            {
                Input = "prompt",
                Stream = stream,
                ModelSelection = new() { ProfileName = "fast" },
                Progress = stream ? (item, _) =>
                {
                    progress.Add(item.Kind);
                    return ValueTask.CompletedTask;
                }
                : null,
                UsageObserver = (item, _) =>
                {
                    usage = item;
                    return ValueTask.CompletedTask;
                },
                OpenAi = new()
                {
                    PreviousResponseId = "prior",
                    PromptCacheKey = "account",
                    CaptureOutputText = true,
                    CaptureRawResponse = true,
                    Cache = new() { Mode = OpenAiCacheMode.Implicit, Ttl = "30m", ComparisonResponseId = "compare" },
                },
            },
        }, TestContext.Current.CancellationToken);
        Assert.Equal("Hello", result.EnsureSuccess());
        Assert.Equal("Text", result.Metadata.Operation);
        Assert.Equal("Hello", result.Metadata.OutputText);
        Assert.NotNull(result.Metadata.RawResponse);
        Assert.Equal("future_type", result.Metadata.CacheDiagnostics!.Type);
        Assert.Equal("future_reason", result.Metadata.CacheDiagnostics.Reason);
        Assert.Equal(50, result.Metadata.CacheDiagnostics.ComparisonReusableTokens);
        Assert.Equal(10, result.Metadata.CacheDiagnostics.CacheMissedTokens);
        Assert.Equal(40, result.Metadata.Usage!.CachedInputTokens);
        Assert.Equal(10, result.Metadata.Usage.CacheWriteTokens);
        Assert.True(usage!.Succeeded);
        if(stream)
            Assert.Equal([StructuredProgressKind.Started, StructuredProgressKind.Completed], progress);
    }

    [Theory]
    [InlineData("incomplete", false, "valid", StructuredErrorKind.IncompleteOutput)]
    [InlineData("failed", false, "valid", StructuredErrorKind.ProviderRejected)]
    [InlineData("queued", false, "valid", StructuredErrorKind.InvalidResponse)]
    [InlineData("completed", true, "valid", StructuredErrorKind.Refused)]
    [InlineData("completed", false, " ", StructuredErrorKind.InvalidResponse)]
    public async Task TextFailuresRetainUsage(string status, bool refusal, string text, StructuredErrorKind expected)
    {
        using var handler = new Handler(_ => Task.FromResult(Response(Envelope(text, status, refusal))));
        using var http = new HttpClient(handler);
        StructuredUsageEvent? observed = null;
        var result = await Client(http).GenerateTextAsync(new()
        {
            Request = new()
            {
                Input = "prompt",
                UsageObserver = (item, _) =>
        {
            observed = item;
            return ValueTask.CompletedTask;
        },
            },
        }, TestContext.Current.CancellationToken);
        Assert.Equal(expected, result.Error!.Kind);
        Assert.Equal(100, result.Metadata.Usage!.InputTokens);
        Assert.False(observed!.Succeeded);
        Assert.Equal(expected, observed.FailureKind);
    }

}
