using Structly.AI.Imaging;
using Structly.AI.Mistral;
using System.Net;
using System.Text.Json;
using static Structly.AI.Tests.MistralFixture;

namespace Structly.AI.Tests;

public sealed class MistralImageTests
{
    const string _envelope = """{"conversation_id":"conversation","outputs":[{"type":"tool.execution","name":"image_generation"},{"type":"message.output","id":"entry","model":"resolved","content":[{"type":"text","text":"Here is the image."},{"type":"tool_file","tool":"image_generation","file_id":"image","file_type":"png"}]}],"usage":{"prompt_tokens":3,"completion_tokens":4,"total_tokens":7}}""";

    [Fact]
    public async Task Images_use_the_builtin_tool_and_download_authenticated_png_bytes()
    {
        byte[] bytes = [137, 80, 78, 71, 13, 10, 26, 10];
        using var handler = new Handler(request => request.Method == HttpMethod.Post ? Handler.Response(_envelope)
            : new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        using var http = new HttpClient(handler);
        StructuredUsageEvent? observed = null;
        var result = await new MistralClient(http, Options()).GenerateImagesAsync(new()
        {
            Prompt = "An orange cat",
            ModelSelection = new() { ModelId = "offline" },
            CaptureRawResponse = true,
            UsageObserver = (usage, _) =>
            {
                observed = usage;
                return ValueTask.CompletedTask;
            },
        }, TestContext.Current.CancellationToken);
        Assert.Equal(bytes, result.EnsureSuccess()[0].ToBytes());
        Assert.Equal("image/png", result.Value![0].MediaType);
        Assert.True(observed!.Succeeded);
        Assert.Equal(7, observed.Usage.TotalTokens);
        Assert.NotNull(result.Metadata.RawResponse);
        Assert.Equal("conversation", result.Metadata.ConversationId);
        Assert.Equal("/v1/conversations", handler.Uris[0].AbsolutePath);
        Assert.Equal("/v1/files/image/content", handler.Uris[1].AbsolutePath);
        using var payload = JsonDocument.Parse(handler.Payloads[0]);
        Assert.Equal("image_generation", payload.RootElement.GetProperty("tools")[0].GetProperty("type").GetString());
        Assert.False(payload.RootElement.GetProperty("store").GetBoolean());
    }

    [Theory]
    [InlineData("not a png", 1024)]
    [InlineData("too large", 1)]
    public async Task Invalid_downloads_keep_generation_usage(string image, int maximum)
    {
        using var handler = new Handler(request => Handler.Response(request.Method == HttpMethod.Post ? _envelope : image));
        using var http = new HttpClient(handler);
        var options = Options();
        options.MaxImageResponseBytes = maximum;
        var result = await new MistralClient(http, options).GenerateImagesAsync(new() { Prompt = "cat", ModelSelection = new() { ModelId = "offline" } }, TestContext.Current.CancellationToken);
        Assert.Equal(StructuredErrorKind.InvalidResponse, result.Error!.Kind);
        Assert.Equal(7, result.Metadata.Usage!.TotalTokens);
    }

    [Fact]
    public async Task Unsupported_image_controls_are_rejected_before_credentials()
    {
        using var http = new HttpClient();
        var options = Options();
        options.CredentialResolver = _ => throw new Exception("Must not resolve");
        var client = new MistralClient(http, options);
        var valid = new ImageGenerationRequest { Prompt = "cat", ModelSelection = new() { ModelId = "offline" } };
        foreach(var request in new[] { valid with { Count = 2 }, valid with { Size = ImageSize.Square }, valid with { Quality = ImageQuality.High },
            valid with { Background = ImageBackground.Transparent }, valid with { Format = ImageFormat.Jpeg }, valid with { IdempotencyKey = "key" }, })
            Assert.Equal(StructuredErrorKind.InvalidRequest, (await client.GenerateImagesAsync(request, TestContext.Current.CancellationToken)).Error!.Kind);
    }

    [Fact]
    public async Task Missing_image_output_fails_without_download_and_keeps_usage()
    {
        using var handler = new Handler(_envelope.Replace("tool_file", "text"));
        using var http = new HttpClient(handler);
        var result = await new MistralClient(http, Options()).GenerateImagesAsync(new() { Prompt = "cat", ModelSelection = new() { ModelId = "offline" } }, TestContext.Current.CancellationToken);
        Assert.Equal(StructuredErrorKind.InvalidResponse, result.Error!.Kind);
        Assert.Equal(7, result.Metadata.Usage!.TotalTokens);
        Assert.Single(handler.Uris);
    }
}
