using Structly.AI.Mistral;
using System.Text.Json;
using static Structly.AI.Tests.MistralFixture;

namespace Structly.AI.Tests;

public sealed class MistralChatTests
{
    [Fact]
    public async Task Vision_reasoning_cache_and_capture_use_documented_fields()
    {
        var envelope = """{"id":"response","model":"resolved","usage":{"prompt_tokens":80,"completion_tokens":4,"total_tokens":84,"prompt_tokens_details":{"cached_tokens":64}},"choices":[{"finish_reason":"stop","message":{"content":[{"type":"thinking","thinking":[{"type":"text","text":"private reasoning"}]},{"type":"text","text":"{\"value\":\"answer\"}"}]}}]}""";
        using var handler = new Handler(envelope);
        using var http = new HttpClient(handler);
        var result = await new MistralClient(http, Options()).ExecuteAsync(StructuredTask.Create<Answer>("Extract"), new MistralRequest
        {
            Messages = [new(MessageRole.User, [new TextPart("Extract from image"), new ImagePart { Url = "https://example.org/image.png", Detail = ImageDetail.High }])],
            ModelSelection = new() { ModelId = "offline", ReasoningEffort = ReasoningEffort.High },
            PromptCacheKey = "session",
            Metadata = new Dictionary<string, string> { ["source"] = "test" },
            CaptureOutputText = true,
            CaptureRawResponse = true,
        }, TestContext.Current.CancellationToken);
        Assert.Equal("answer", result.EnsureSuccess().Value);
        Assert.Null(result.Metadata.ReasoningSummary);
        Assert.Equal(64, result.Metadata.Usage!.CachedInputTokens);
        Assert.Equal("{\"value\":\"answer\"}", result.Metadata.OutputText);
        Assert.NotNull(result.Metadata.RawResponse);
        using var document = JsonDocument.Parse(handler.Payloads[0]);
        var root = document.RootElement;
        Assert.Equal("high", root.GetProperty("reasoning_effort").GetString());
        Assert.Equal("session", root.GetProperty("prompt_cache_key").GetString());
        Assert.Equal("test", root.GetProperty("metadata").GetProperty("source").GetString());
        Assert.Equal("high", root.GetProperty("messages")[1].GetProperty("content")[1].GetProperty("image_url").GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Base64_images_are_sent_without_downloading()
    {
        using var handler = new Handler(Chat);
        using var http = new HttpClient(handler);
        var image = ImagePart.FromBytes([137, 80, 78, 71, 13, 10, 26, 10]);
        var result = await new MistralClient(http, Options()).GenerateTextAsync(new()
        {
            Request = new() { Messages = [new(MessageRole.User, [image])] },
        }, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Single(handler.Uris);
        Assert.Contains(image.Url, handler.Payloads[0]);
    }

    [Fact]
    public async Task Unsupported_controls_fail_before_credentials()
    {
        using var http = new HttpClient();
        var options = Options();
        options.CredentialResolver = _ => throw new Exception("Must not resolve");
        var client = new MistralClient(http, options);
        var requests = new StructuredRequest[]
        {
            new() { Input = "input", Progress = (_, _) => ValueTask.CompletedTask },
            new() { Input = "input", InactivityTimeout = TimeSpan.FromSeconds(1) },
            new MistralRequest { Input = "input", Stream = true, CaptureRawResponse = true },
            new MistralRequest { Input = "input", PromptCacheKey = " " },
            new() { Messages = [new(MessageRole.Developer, [new TextPart("instructions")])] },
            new() { Messages = [new(MessageRole.User, [new TextPart("input") { CacheBreakpoint = true }])] },
            new() { Messages = [new(MessageRole.User, [new ImagePart { Url = "data:image/png;base64,invalid" }])] },
            new() { Messages = [new(MessageRole.Assistant, [new ImagePart { Url = "https://example.org/image.png" }])] },
            new() { Messages = [new(MessageRole.User, [new ImagePart { Url = "https://secret@example.org/image.png" }])] },
        };
        foreach(var request in requests)
            Assert.Equal(StructuredErrorKind.InvalidRequest, (await client.GenerateTextAsync(new() { Request = request }, TestContext.Current.CancellationToken)).Error!.Kind);
    }
}
