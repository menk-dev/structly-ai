using Structly.AI.Imaging;
using Structly.AI.OpenAI;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Structly.AI.Tests.EmbeddingAndImageTestSupport;

namespace Structly.AI.Tests;

public sealed class ImageGenerationTests
{
    [Theory]
    [InlineData(ImageSize.Auto, "auto")]
    [InlineData(ImageSize.Square, "1024x1024")]
    [InlineData(ImageSize.Portrait, "1024x1536")]
    [InlineData(ImageSize.Landscape, "1536x1024")]
    [InlineData(ImageSize.Wide, "1536x864")]
    public async Task ImagePresetsAndOptionsMatchWire(ImageSize size, string expected)
    {
        using var handler = new Handler(async message =>
        {
            Assert.EndsWith("/images/generations", message.RequestUri!.AbsoluteUri);
            using var document = JsonDocument.Parse(await message.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken));
            var root = document.RootElement;
            Assert.Equal("image", root.GetProperty("model").GetString());
            Assert.Equal("draw a house", root.GetProperty("prompt").GetString());
            Assert.Equal(expected, root.GetProperty("size").GetString());
            Assert.Equal("high", root.GetProperty("quality").GetString());
            Assert.Equal("transparent", root.GetProperty("background").GetString());
            Assert.Equal("webp", root.GetProperty("output_format").GetString());
            Assert.Equal(2, root.GetProperty("n").GetInt32());
            Assert.False(root.TryGetProperty("response_format", out _));
            return Response(ImageEnvelope("webp", 2));
        });
        using var http = new HttpClient(handler);
        StructuredUsageEvent? usage = null;
        var result = await Client(http).GenerateImagesAsync(Image() with
        {
            Size = size,
            Quality = ImageQuality.High,
            Background = ImageBackground.Transparent,
            Format = ImageFormat.WebP,
            Count = 2,
            UsageObserver = (item, _) =>
            {
                usage = item;
                return ValueTask.CompletedTask;
            },
        }, TestContext.Current.CancellationToken);
        Assert.Equal(2, result.EnsureSuccess().Count);
        Assert.All(result.Value!, image =>
        {
            Assert.Equal("image/webp", image.MediaType);
            Assert.Equal(Convert.FromBase64String(Base64("webp")), image.ToBytes());
        });
        Assert.Throws<NotSupportedException>(() => ((IList<GeneratedImage>)result.Value!)[0] = new("AQ==", "image/png"));
        Assert.Equal("Images", result.Metadata.Operation);
        Assert.Equal("image", result.Metadata.RequestedModel);
        Assert.Null(result.Metadata.ResolvedModel);
        Assert.Null(result.Metadata.ResponseId);
        Assert.Equal(6, result.Metadata.Usage!.InputTextTokens);
        Assert.Equal(4, result.Metadata.Usage.InputImageTokens);
        Assert.Equal(18, result.Metadata.Usage.OutputImageTokens);
        Assert.Equal(2, result.Metadata.Usage.OutputTextTokens);
        Assert.Equal(30, usage!.Metadata.Usage!.TotalTokens);
    }

    [Theory]
    [InlineData(ImageFormat.Png, "png")]
    [InlineData(ImageFormat.Jpeg, "jpeg")]
    [InlineData(ImageFormat.WebP, "webp")]
    public async Task ImageCustomDimensionsOverridePresetAndFormatMatchesBytes(ImageFormat format, string wire)
    {
        using var handler = new Handler(async message =>
        {
            using var document = JsonDocument.Parse(await message.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.Equal("3072x2048", document.RootElement.GetProperty("size").GetString());
            return Response(ImageEnvelope(wire));
        });
        using var http = new HttpClient(handler);
        var result = await Client(http).GenerateImagesAsync(Image() with { Format = format, CustomDimensions = new(3072, 2048), Size = ImageSize.Square }, TestContext.Current.CancellationToken);
        Assert.Equal("image/" + wire, Assert.Single(result.EnsureSuccess()).MediaType);
    }

    [Theory]
    [InlineData("prompt")]
    [InlineData("count-zero")]
    [InlineData("count-large")]
    [InlineData("size")]
    [InlineData("dimensions")]
    [InlineData("quality")]
    [InlineData("background")]
    [InlineData("format")]
    [InlineData("transparent-jpeg")]
    [InlineData("profile")]
    [InlineData("reasoning")]
    [InlineData("timeout")]
    public async Task InvalidImageControlsAreSuppressed(string scenario)
    {
        var auth = 0;
        var request = Image() with
        {
            CredentialResolver = _ =>
        {
            auth++;
            return ValueTask.FromResult<string?>("key");
        },
        };
        request = scenario switch
        {
            "prompt" => request with { Prompt = " " },
            "count-zero" => request with { Count = 0 },
            "count-large" => request with { Count = 11 },
            "size" => request with { Size = (ImageSize)100 },
            "dimensions" => request with { CustomDimensions = new(0, 1024) },
            "quality" => request with { Quality = (ImageQuality)100 },
            "background" => request with { Background = (ImageBackground)100 },
            "format" => request with { Format = (ImageFormat)100 },
            "transparent-jpeg" => request with { Format = ImageFormat.Jpeg, Background = ImageBackground.Transparent },
            "profile" => request with { ModelSelection = new() { ProfileName = "unknown" } },
            "reasoning" => request with { ModelSelection = new() { ProfileName = "balanced", ReasoningEffort = ReasoningEffort.Low } },
            _ => request with { TotalTimeout = TimeSpan.Zero },
        };
        using var handler = new Handler(_ => throw new InvalidOperationException("Should not send"));
        using var http = new HttpClient(handler);
        var result = await Client(http).GenerateImagesAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(StructuredErrorKind.InvalidRequest, result.Error!.Kind);
        Assert.Equal(0, auth);
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData("bad-base64")]
    [InlineData("empty")]
    [InlineData("wrong-bytes")]
    [InlineData("wrong-reported-format")]
    [InlineData("url-only")]
    [InlineData("count")]
    [InlineData("data-shape")]
    public async Task ImageProcessingFailuresRetainUsageAndNotifyObserver(string scenario)
    {
        var root = JsonNode.Parse(ImageEnvelope())!;
        switch(scenario)
        {
            case "bad-base64":
                root["data"]![0]!["b64_json"] = "private!!";
                break;
            case "empty":
                root["data"]![0]!["b64_json"] = "";
                break;
            case "wrong-bytes":
                root["data"]![0]!["b64_json"] = Base64("jpeg");
                break;
            case "wrong-reported-format":
                root["output_format"] = "webp";
                break;
            case "url-only":
                root["data"]![0] = new JsonObject { ["url"] = "https://example.test/private.png" };
                break;
            case "count":
                root["data"] = new JsonArray();
                break;
            case "data-shape":
                root["data"] = new JsonObject();
                break;
        }

        using var handler = new Handler(_ => Task.FromResult(Response(root.ToJsonString())));
        using var http = new HttpClient(handler);
        StructuredUsageEvent? usage = null;
        var result = await Client(http).GenerateImagesAsync(Image() with
        {
            CaptureRawResponse = true,
            UsageObserver = (item, _) =>
        {
            usage = item;
            return ValueTask.CompletedTask;
        },
        }, TestContext.Current.CancellationToken);
        Assert.Equal(StructuredErrorKind.InvalidResponse, result.Error!.Kind);
        Assert.Equal(30, result.Metadata.Usage!.TotalTokens);
        Assert.NotNull(result.Metadata.RawResponse);
        Assert.False(usage!.Succeeded);
        Assert.Equal(StructuredErrorKind.InvalidResponse, usage.FailureKind);
        Assert.DoesNotContain("private", result.Error.Message);
    }

    [Fact]
    public async Task ImageOrderIsPreservedAndAbsentFormatUsesVerifiedFileSignature()
    {
        var first = Convert.FromBase64String(Base64("png"));
        var second = first.ToArray();
        second[^1] = 1;
        var body = JsonSerializer.Serialize(new { data = new[] { new { b64_json = Convert.ToBase64String(first) }, new { b64_json = Convert.ToBase64String(second) } } });
        using var handler = new Handler(_ => Task.FromResult(Response(body)));
        using var http = new HttpClient(handler);
        var images = (await Client(http).GenerateImagesAsync(Image() with { Count = 2 }, TestContext.Current.CancellationToken)).EnsureSuccess();
        Assert.Equal(first, images[0].ToBytes());
        Assert.Equal(second, images[1].ToBytes());
    }

    [Fact]
    public async Task ImageResponseLimitIsIndependentAndEnforced()
    {
        using var handler = new Handler(_ => Task.FromResult(Response(ImageEnvelope())));
        using var http = new HttpClient(handler);
        var options = new OpenAiClientOptions
        {
            DefaultModel = new() { ModelId = "response" },
            CredentialResolver = Credentials.FromStatic("key"),
            MaxResponseBytes = 1,
            MaxImageResponseBytes = 1024,
        };
        var request = Image() with { ModelSelection = new() { ModelId = "image" } };
        Assert.True((await new OpenAiClient(http, options).GenerateImagesAsync(request, TestContext.Current.CancellationToken)).IsSuccess);
        var tooLarge = await new OpenAiClient(http, options with { MaxImageResponseBytes = 1 }).GenerateImagesAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(StructuredErrorKind.InvalidResponse, tooLarge.Error!.Kind);
        Assert.Null(tooLarge.Metadata.Usage);
        Assert.Throws<ArgumentException>(() => new OpenAiClient(http, options with { MaxImageResponseBytes = 0 }));
    }

}
