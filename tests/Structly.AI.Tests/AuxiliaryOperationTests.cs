using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Structly.AI.Embeddings;
using Structly.AI.Imaging;
using Structly.AI.OpenAI;

namespace Structly.AI.Tests;

public sealed class AuxiliaryOperationTests
{
    static ModelSelection Model => new() { ProfileName = "balanced" };
    static OpenAiClient Client(HttpClient http, TimeProvider? clock = null) => new(http, new()
    {
        DefaultModel = new() { ModelId = "response" },
        CredentialResolver = Credentials.FromStatic("client-key"),
        EmbeddingProfiles = new Dictionary<string, ModelSelection> { ["balanced"] = new() { ModelId = "embedding" } },
        ImageProfiles = new Dictionary<string, ModelSelection> { ["balanced"] = new() { ModelId = "image" } },
        TimeProvider = clock ?? TimeProvider.System
    });
    static EmbeddingRequest Embedding() => new() { Inputs = ["first", "second"], ModelSelection = Model, Dimensions = 2 };
    static ImageGenerationRequest Image() => new() { Prompt = "draw a house", ModelSelection = Model };
    static string EmbeddingEnvelope() => """
        {"model":"embedding-resolved","data":[{"index":1,"embedding":[3,4]},{"index":0,"embedding":[1,2]}],"usage":{"prompt_tokens":7,"total_tokens":7}}
        """;
    static string ImageEnvelope(string format = "png", int count = 1) => JsonSerializer.Serialize(new
    {
        output_format = format,
        data = Enumerable.Range(0, count).Select(_ => new { b64_json = Base64(format) }),
        usage = new
        {
            input_tokens = 10,
            output_tokens = 20,
            total_tokens = 30,
            input_tokens_details = new { text_tokens = 6, image_tokens = 4 },
            output_tokens_details = new { image_tokens = 18, text_tokens = 2 }
        }
    });
    static string Base64(string format) => Convert.ToBase64String(format switch
    { "jpeg" => [255, 216, 255, 0], "webp" => "RIFFabcdWEBPdata"u8.ToArray(), _ => [137, 80, 78, 71, 13, 10, 26, 10, 0] });
    static HttpResponseMessage Response(string body, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(body) };

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
            UsageObserver = (item, _) => { observed = item; return ValueTask.CompletedTask; }
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
        switch (scenario)
        {
            case "duplicate": root["data"]![1]!["index"] = 1; break;
            case "negative": root["data"]![0]!["index"] = -1; break;
            case "outside": root["data"]![0]!["index"] = 2; break;
            case "fractional": root["data"]![0]!["index"] = 0.5; break;
            case "missing": ((JsonArray)root["data"]!).RemoveAt(0); break;
            case "empty-vector": root["data"]![0]!["embedding"] = new JsonArray(); break;
            case "dimension-mismatch":
            case "unequal-vectors": root["data"]![0]!["embedding"] = new JsonArray(1); break;
            case "string-component": root["data"]![0]!["embedding"]![0] = "secret"; break;
            case "null-component": root["data"]![0]!["embedding"]![0] = null; break;
            case "infinite": root["data"]![0]!["embedding"]![0] = JsonNode.Parse("1e100"); break;
            case "missing-model": ((JsonObject)root).Remove("model"); break;
            case "non-array": root["data"] = new JsonObject(); break;
        }
        using var handler = new Handler(_ => Task.FromResult(Response(root.ToJsonString())));
        using var http = new HttpClient(handler);
        StructuredUsageEvent? usage = null;
        var request = Embedding() with { Dimensions = scenario == "unequal-vectors" ? null : 2, UsageObserver = (item, _) => { usage = item; return ValueTask.CompletedTask; } };
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
        var request = Embedding() with { CredentialResolver = _ => { auth++; return ValueTask.FromResult<string?>("key"); } };
        request = scenario switch
        {
            "empty" => request with { Inputs = [] },
            "blank" => request with { Inputs = [" "] },
            "dimensions" => request with { Dimensions = 0 },
            "model" => request with { ModelSelection = new() { ProfileName = "unknown" } },
            "reasoning" => request with { ModelSelection = new() { ProfileName = "balanced", ReasoningEffort = ReasoningEffort.High } },
            "timeout" => request with { TotalTimeout = TimeSpan.Zero },
            _ => request with { IdempotencyKey = "bad\r\nheader" }
        };
        using var handler = new Handler(_ => throw new InvalidOperationException("Should not send"));
        using var http = new HttpClient(handler);
        var result = await Client(http).EmbedAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(StructuredErrorKind.InvalidRequest, result.Error!.Kind);
        Assert.Equal(0, auth);
        Assert.Equal(0, handler.Calls);
    }

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
            UsageObserver = (item, _) => { usage = item; return ValueTask.CompletedTask; }
        }, TestContext.Current.CancellationToken);
        Assert.Equal(2, result.EnsureSuccess().Count);
        Assert.All(result.Value!, image => { Assert.Equal("image/webp", image.MediaType); Assert.Equal(Convert.FromBase64String(Base64("webp")), image.ToBytes()); });
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
        var request = Image() with { CredentialResolver = _ => { auth++; return ValueTask.FromResult<string?>("key"); } };
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
            _ => request with { TotalTimeout = TimeSpan.Zero }
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
        switch (scenario)
        {
            case "bad-base64": root["data"]![0]!["b64_json"] = "private!!"; break;
            case "empty": root["data"]![0]!["b64_json"] = ""; break;
            case "wrong-bytes": root["data"]![0]!["b64_json"] = Base64("jpeg"); break;
            case "wrong-reported-format": root["output_format"] = "webp"; break;
            case "url-only": root["data"]![0] = new JsonObject { ["url"] = "https://example.test/private.png" }; break;
            case "count": root["data"] = new JsonArray(); break;
            case "data-shape": root["data"] = new JsonObject(); break;
        }
        using var handler = new Handler(_ => Task.FromResult(Response(root.ToJsonString())));
        using var http = new HttpClient(handler);
        StructuredUsageEvent? usage = null;
        var result = await Client(http).GenerateImagesAsync(Image() with { CaptureRawResponse = true, UsageObserver = (item, _) => { usage = item; return ValueTask.CompletedTask; } }, TestContext.Current.CancellationToken);
        Assert.Equal(StructuredErrorKind.InvalidResponse, result.Error!.Kind);
        Assert.Equal(30, result.Metadata.Usage!.TotalTokens);
        Assert.NotNull(result.Metadata.RawResponse);
        Assert.False(usage!.Succeeded);
        Assert.Equal(StructuredErrorKind.InvalidResponse, usage.FailureKind);
        Assert.DoesNotContain("private", result.Error.Message);
    }

    [Theory]
    [InlineData(false, 401, StructuredErrorKind.Authentication)]
    [InlineData(true, 403, StructuredErrorKind.PermissionDenied)]
    [InlineData(false, 429, StructuredErrorKind.RateLimited)]
    [InlineData(true, 500, StructuredErrorKind.ProviderUnavailable)]
    [InlineData(false, 400, StructuredErrorKind.ProviderRejected)]
    public async Task AuxiliaryHttpFailuresUseSharedTaxonomyAndOneAttempt(bool image, int status, StructuredErrorKind expected)
    {
        using var handler = new Handler(_ =>
        {
            var response = Response("{\"error\":{\"code\":\"private\"},\"usage\":{\"total_tokens\":4}}", (HttpStatusCode)status);
            response.Headers.RetryAfter = new(TimeSpan.FromSeconds(3));
            return Task.FromResult(response);
        });
        using var http = new HttpClient(handler);
        var client = Client(http);
        var error = image ? (await client.GenerateImagesAsync(Image(), TestContext.Current.CancellationToken)).Error : (await client.EmbedAsync(Embedding(), TestContext.Current.CancellationToken)).Error;
        Assert.Equal(expected, error!.Kind);
        Assert.Equal(TimeSpan.FromSeconds(3), error.RetryAfter);
        Assert.DoesNotContain("private", error.Message);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("text")]
    [InlineData("prewarm")]
    [InlineData("embedding")]
    [InlineData("image")]
    public async Task AllNewOperationsHonorPreCancellation(string operation)
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        using var handler = new Handler(_ => throw new InvalidOperationException("Should not send"));
        using var http = new HttpClient(handler);
        var client = Client(http);
        var exception = await Assert.ThrowsAsync<StructuredOperationCanceledException>(async () =>
        {
            switch (operation)
            {
                case "text": await client.GenerateTextAsync(new() { Request = new() { Input = "prompt" } }, cancelled.Token); break;
                case "prewarm": await client.PrewarmAsync(new() { Request = new() { Input = "prompt" } }, cancelled.Token); break;
                case "embedding": await client.EmbedAsync(Embedding(), cancelled.Token); break;
                default: await client.GenerateImagesAsync(Image(), cancelled.Token); break;
            }
        });
        Assert.Equal(cancelled.Token, exception.CancellationToken);
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AuxiliaryOperationsShareBoundedNoncooperativeSendAndLateCleanup(bool image)
    {
        var clock = new ManualClock();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var responseGate = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new Handler(_ => { entered.SetResult(); return responseGate.Task; });
        using var http = new HttpClient(handler);
        var client = Client(http, clock);
        var pending = image ? Observe(client.GenerateImagesAsync(Image() with { TotalTimeout = TimeSpan.FromSeconds(1) }, TestContext.Current.CancellationToken))
            : Observe(client.EmbedAsync(Embedding() with { TotalTimeout = TimeSpan.FromSeconds(1) }, TestContext.Current.CancellationToken));
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(StructuredErrorKind.DeadlineExceeded, await pending.WaitAsync(TestContext.Current.CancellationToken));
        using var content = new TrackedContent();
        responseGate.SetResult(new(HttpStatusCode.OK) { Content = content });
        await content.Disposed.Task.WaitAsync(TestContext.Current.CancellationToken);
    }

    static async Task<StructuredErrorKind?> Observe<T>(Task<StructuredResult<T>> pending) => (await pending).Error?.Kind;

    [Fact]
    public async Task ConcurrentAuxiliaryBatchesSnapshotInputsAndIsolateCredentialsProfilesAndObservers()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var inputs = new List<string> { "first", "second" };
        var observers = new List<string>();
        using var handler = new Handler(async message =>
        {
            using var document = JsonDocument.Parse(await message.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken));
            var root = document.RootElement;
            if (message.RequestUri!.AbsolutePath.EndsWith("embeddings", StringComparison.Ordinal))
            {
                Assert.Equal("embed-key", message.Headers.Authorization!.Parameter);
                Assert.Equal("first", root.GetProperty("input")[0].GetString());
                Assert.Equal("embedding", root.GetProperty("model").GetString());
                return Response(EmbeddingEnvelope());
            }
            Assert.Equal("image-key", message.Headers.Authorization!.Parameter);
            Assert.Equal("image", root.GetProperty("model").GetString());
            return Response(ImageEnvelope());
        });
        using var http = new HttpClient(handler);
        var client = Client(http);
        var embed = client.EmbedAsync(Embedding() with
        {
            Inputs = inputs,
            CredentialResolver = _ => { entered.SetResult(); return new(release.Task); },
            UsageObserver = (item, _) => { observers.Add(item.Metadata.Operation!); return ValueTask.CompletedTask; }
        }, TestContext.Current.CancellationToken);
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        inputs[0] = "mutated";
        var image = await client.GenerateImagesAsync(Image() with
        {
            CredentialResolver = Credentials.FromStatic("image-key"),
            UsageObserver = (item, _) => { observers.Add(item.Metadata.Operation!); return ValueTask.CompletedTask; }
        }, TestContext.Current.CancellationToken);
        release.SetResult("embed-key");
        Assert.True((await embed).IsSuccess);
        Assert.True(image.IsSuccess);
        Assert.Equal(new[] { "Images", "Embeddings" }, observers);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BilledAuxiliaryCancellationRetainsUsageAndObserverFailure(bool image)
    {
        using var caller = new CancellationTokenSource();
        using var handler = new Handler(_ => Task.FromResult(Response(image ? ImageEnvelope() : EmbeddingEnvelope())));
        using var http = new HttpClient(handler);
        var client = Client(http);
        ValueTask Observer(StructuredUsageEvent item, CancellationToken token)
        {
            Assert.True(item.Succeeded);
            caller.Cancel();
            Assert.False(token.IsCancellationRequested);
            return ValueTask.FromException(new InvalidOperationException("private"));
        }
        var exception = await Assert.ThrowsAsync<StructuredOperationCanceledException>(async () =>
        {
            if (image) await client.GenerateImagesAsync(Image() with { UsageObserver = Observer }, caller.Token);
            else await client.EmbedAsync(Embedding() with { UsageObserver = Observer }, caller.Token);
        });
        Assert.Equal(image ? 30 : 7, exception.Metadata.Usage!.TotalTokens);
        Assert.Equal(caller.Token, exception.CancellationToken);
        Assert.Contains(exception.Warnings, warning => warning.Code == "UsageObserverFailed");
        Assert.DoesNotContain(exception.Warnings, warning => warning.Message.Contains("private", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SelectedAuxiliaryCredentialsNeverFallBack(bool image)
    {
        using var handler = new Handler(_ => throw new InvalidOperationException("Should not send"));
        using var http = new HttpClient(handler);
        var client = Client(http);
        Func<CancellationToken, ValueTask<string?>> missing = _ => ValueTask.FromResult<string?>(null);
        var error = image ? (await client.GenerateImagesAsync(Image() with { CredentialResolver = missing }, TestContext.Current.CancellationToken)).Error
            : (await client.EmbedAsync(Embedding() with { CredentialResolver = missing }, TestContext.Current.CancellationToken)).Error;
        Assert.Equal(StructuredErrorKind.CredentialsMissing, error!.Kind);
        Assert.Equal(0, handler.Calls);
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
            MaxImageResponseBytes = 1024
        };
        var request = Image() with { ModelSelection = new() { ModelId = "image" } };
        Assert.True((await new OpenAiClient(http, options).GenerateImagesAsync(request, TestContext.Current.CancellationToken)).IsSuccess);
        var tooLarge = await new OpenAiClient(http, options with { MaxImageResponseBytes = 1 }).GenerateImagesAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(StructuredErrorKind.InvalidResponse, tooLarge.Error!.Kind);
        Assert.Null(tooLarge.Metadata.Usage);
        Assert.Throws<ArgumentException>(() => new OpenAiClient(http, options with { MaxImageResponseBytes = 0 }));
    }

    sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        int _calls;
        public int Calls => Volatile.Read(ref _calls);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Interlocked.Increment(ref _calls); return send(request); }
    }
    sealed class TrackedContent : StringContent
    {
        public TrackedContent() : base("{}") { }
        public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override void Dispose(bool disposing) { base.Dispose(disposing); if (disposing) Disposed.TrySetResult(); }
    }
    sealed class ManualClock : TimeProvider
    {
        long _ticks;
        readonly List<ManualTimer> _timers = [];
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddTicks(_ticks);
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state);
            timer.Change(dueTime, period);
            _timers.Add(timer);
            return timer;
        }
        public void Advance(TimeSpan duration)
        {
            _ticks += duration.Ticks;
            foreach (var timer in _timers.ToArray()) timer.Fire();
        }
        sealed class ManualTimer(ManualClock clock, TimerCallback callback, object? state) : ITimer
        {
            long _due = Int64.MaxValue;
            public bool Change(TimeSpan dueTime, TimeSpan period) { _due = dueTime == Timeout.InfiniteTimeSpan ? Int64.MaxValue : clock._ticks + dueTime.Ticks; return true; }
            public void Fire() { if (clock._ticks < _due) return; _due = Int64.MaxValue; callback(state); }
            public void Dispose() => _due = Int64.MaxValue;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
