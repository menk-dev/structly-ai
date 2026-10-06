using Structly.AI.OpenAI;
using System.Text.Json;
using static Structly.AI.Tests.ResponseTestSupport;

namespace Structly.AI.Tests;

public sealed class PrewarmTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PrewarmUsesMetadataOnlyPipelineAndOptionalCurrentSchema(bool typed)
    {
        using var handler = new Handler(async message =>
        {
            using var document = JsonDocument.Parse(await message.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken));
            var root = document.RootElement;
            Assert.True(root.GetProperty("prompt_cache_options").GetProperty("prewarm").GetBoolean());
            Assert.False(root.GetProperty("stream").GetBoolean());
            Assert.False(root.GetProperty("store").GetBoolean());
            Assert.False(root.TryGetProperty("max_output_tokens", out _));
            Assert.Equal(typed, root.TryGetProperty("text", out _));
            Assert.Equal(typed ? "Current instructions" : "Shared instructions", root.GetProperty("instructions").GetString());
            if(typed)
                Assert.Equal(Contract<Guided>().CreateSchema(Vocabulary("a")).GetRawText(), root.GetProperty("text").GetProperty("format").GetProperty("schema").GetRawText());

            return Response(Envelope(prewarm: true));
        });
        using var http = new HttpClient(handler);
        var client = Client(http);
        var warm = new OpenAiPrewarmRequest { Instructions = typed ? null : "Shared instructions", Request = new() { Input = "stable prefix", Vocabularies = typed ? Vocabulary("a") : null } };
        var result = typed ? await client.PrewarmAsync(Contract<Guided>(), warm, TestContext.Current.CancellationToken) : await client.PrewarmAsync(warm, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Equal("Prewarm", result.Metadata.Operation);
        Assert.Equal(100, result.Metadata.Usage!.InputTokens);
        Assert.Equal("current", result.Metadata.ResponseId);
        Assert.Null(result.Metadata.OutputText);
    }

    [Theory]
    [InlineData("stream")]
    [InlineData("progress")]
    [InlineData("summary")]
    [InlineData("cap")]
    [InlineData("continue")]
    [InlineData("store")]
    [InlineData("capture")]
    [InlineData("guidance")]
    [InlineData("legacy")]
    public async Task PrewarmRejectsGenerationAndContinuation(string scenario)
    {
        using var handler = new Handler(_ => throw new InvalidOperationException("Should not send"));
        using var http = new HttpClient(handler);
        var request = new OpenAiRequest { Input = "prefix" };
        request = scenario switch
        {
            "stream" => request with { Stream = true },
            "progress" => request with { Progress = (_, _) => ValueTask.CompletedTask },
            "summary" => request with { IncludeReasoningSummary = true },
            "cap" => request with { MaxOutputTokens = 1 },
            "continue" => request with { OpenAi = new() { PreviousResponseId = "prior" } },
            "store" => request with { OpenAi = new() { Store = true } },
            "capture" => request with { OpenAi = new() { CaptureOutputText = true } },
            "guidance" => request with { OutputSpecification = new() },
            _ => request,
        };
        var result = await Client(http, scenario != "legacy").PrewarmAsync(new() { Request = request }, TestContext.Current.CancellationToken);
        Assert.Equal(StructuredErrorKind.InvalidRequest, result.Error!.Kind);
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData("failed", false, StructuredErrorKind.ProviderRejected)]
    [InlineData("incomplete", false, StructuredErrorKind.IncompleteOutput)]
    [InlineData("queued", false, StructuredErrorKind.InvalidResponse)]
    [InlineData("completed", true, StructuredErrorKind.InvalidResponse)]
    public async Task PrewarmRequiresCompletedMetadataWithoutGeneratedOutput(string status, bool generated, StructuredErrorKind expected)
    {
        using var handler = new Handler(_ => Task.FromResult(Response(Envelope(status: status, prewarm: !generated))));
        using var http = new HttpClient(handler);
        var result = await Client(http).PrewarmAsync(new() { Request = new() { Input = "prefix" } }, TestContext.Current.CancellationToken);
        Assert.Equal(expected, result.Error!.Kind);
        Assert.Equal(100, result.Metadata.Usage!.InputTokens);
    }

}
