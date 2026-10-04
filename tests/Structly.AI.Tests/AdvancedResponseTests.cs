using System.ComponentModel;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Structly.AI.OpenAI;

namespace Structly.AI.Tests;

public sealed class AdvancedResponseTests
{
    static StructuredTask<T> Contract<T>() => StructuredTask.Create<T>(new() { Instructions = "Current instructions" });
    static OpenAiClient Client(HttpClient http, bool modern = true) => new(http, new()
    {
        DefaultModel = new() { ModelId = "response-model" },
        CredentialResolver = Credentials.FromStatic("offline"),
        Profiles = new Dictionary<string, ModelSelection> { ["fast"] = new() { ModelId = "response-model", ReasoningEffort = ReasoningEffort.Low } },
        CacheCompatibility = new Dictionary<string, OpenAiCacheCompatibility> { ["response-model"] = modern ? OpenAiCacheCompatibility.Modern : OpenAiCacheCompatibility.Legacy }
    });
    static string Envelope(string text = "Hello", string status = "completed", bool refusal = false, bool prewarm = false) => JsonSerializer.Serialize(new
    {
        id = "current",
        model = "resolved",
        status,
        output = prewarm ? [] : new object[] { new { type = "message", role = "assistant", status = "completed", content = refusal
            ? new object[] { new { type = "refusal", refusal = "private" } } : [new { type = "output_text", text }] } },
        usage = new { input_tokens = 100, output_tokens = 3, input_tokens_details = new { cached_tokens = 40, cache_write_tokens = 10 } },
        prompt_cache_diagnostics = new { type = "future_type", reason = "future_reason", comparison_reusable_tokens = 50, cache_missed_tokens = 10 }
    });
    static HttpResponseMessage Response(string body, bool stream = false) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(stream ? "event: response.completed\ndata: " + JsonSerializer.Serialize(new { type = "response.completed", response = JsonSerializer.Deserialize<JsonElement>(body) }) + "\n\n" : body,
        Encoding.UTF8, stream ? "text/event-stream" : "application/json")
    };
    static IReadOnlyDictionary<string, IReadOnlyList<string>> Vocabulary(params string[] values) => new Dictionary<string, IReadOnlyList<string>> { ["choices"] = values };

    [Fact]
    public void GuidanceUsesSerializedContractAndValidatesSample()
    {
        var task = Contract<Guided>();
        var guidance = task.CreateOutputSpecification(new(), Vocabulary("z", "a"));
        Assert.Contains(task.CreateSchema(Vocabulary("a", "z")).GetRawText(), guidance);
        Assert.Contains("- $.renamed: string", guidance);
        Assert.Contains("Explain this field", guidance);
        Assert.Contains("- $.items[]: string or null", guidance);
        var json = guidance.Split("Example of valid output JSON:\n")[1];
        var sample = task.ReadOutput(json, Vocabulary("a", "z")).EnsureSuccess();
        Assert.Equal("a", sample.Choice);
        Assert.Equal("xxx", sample.Label);
        Assert.Equal(2, sample.Items.Count);
        Assert.All(sample.Items, item => Assert.Null(item));
        Assert.InRange(sample.Number, -5, -2);
        Assert.Null(sample.Optional);
        Assert.Equal(guidance, task.CreateOutputSpecification(new(), Vocabulary("z", "a")));
    }

    [Fact]
    public void GuidanceSwitchesAndCallerExamplesAreEnforced()
    {
        var task = Contract<Patterned>();
        Assert.Throws<ArgumentException>(() => task.CreateOutputSpecification(new()));
        var example = "{\"code\":\"AB\"}";
        var guidance = task.CreateOutputSpecification(new() { IncludeFields = false, ExampleJson = example, AdditionalInstructions = "Use this style" });
        Assert.Equal("Example of valid output JSON:\n" + example + "\n\nUse this style", guidance);
        Assert.Throws<ArgumentException>(() => task.CreateOutputSpecification(new() { ExampleJson = "{\"code\":\"xx\"}" }));
        Assert.Throws<ArgumentException>(() => task.CreateOutputSpecification(new() { IncludeExample = false, ExampleJson = example }));
        Assert.Throws<ArgumentException>(() => task.CreateOutputSpecification(new() { AdditionalInstructions = " " }));
        Assert.Contains(task.CreateSchema().GetRawText(), task.CreateOutputSpecification(new() { IncludeExample = false }));
        Assert.Equal("", task.CreateOutputSpecification(new() { IncludeFields = false, IncludeExample = false }));
    }

    [Fact]
    public void GeneratedSamplesBoundExpandedCollectionWork()
    {
        var task = Contract<LargeSample>();
        Assert.Throws<ArgumentException>(() => task.CreateOutputSpecification(new()));
        Assert.Contains(task.CreateSchema().GetRawText(), task.CreateOutputSpecification(new() { IncludeExample = false }));
    }

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
            Assert.False(root.GetProperty("store").GetBoolean());
            Assert.Equal("low", root.GetProperty("reasoning").GetProperty("effort").GetString());
            Assert.Equal("compare", root.GetProperty("prompt_cache_options").GetProperty("comparison_response_id").GetString());
            Assert.Equal("account", root.GetProperty("prompt_cache_key").GetString());
            return Response(Envelope(), stream);
        });
        using var http = new HttpClient(handler);
        var result = await Client(http).GenerateTextAsync(new()
        {
            Instructions = "new instructions",
            Request = new()
            {
                Input = "prompt",
                Stream = stream,
                ModelSelection = new() { ProfileName = "fast" },
                Progress = stream ? (item, _) => { progress.Add(item.Kind); return ValueTask.CompletedTask; }
                : null,
                UsageObserver = (item, _) => { usage = item; return ValueTask.CompletedTask; },
                OpenAi = new()
                {
                    PreviousResponseId = "prior",
                    PromptCacheKey = "account",
                    CaptureOutputText = true,
                    CaptureRawResponse = true,
                    Cache = new() { Mode = OpenAiCacheMode.Implicit, Ttl = "30m", ComparisonResponseId = "compare" }
                }
            }
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
        if (stream) Assert.Equal([StructuredProgressKind.Started, StructuredProgressKind.Completed], progress);
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
        var result = await Client(http).GenerateTextAsync(new() { Request = new() { Input = "prompt", UsageObserver = (item, _) => { observed = item; return ValueTask.CompletedTask; } } }, TestContext.Current.CancellationToken);
        Assert.Equal(expected, result.Error!.Kind);
        Assert.Equal(100, result.Metadata.Usage!.InputTokens);
        Assert.False(observed!.Succeeded);
        Assert.Equal(expected, observed.FailureKind);
    }

    [Fact]
    public async Task MultimodalRolesOrderGuidanceAndBreakpointsMatchWire()
    {
        var task = Contract<Guided>();
        var vocabulary = Vocabulary("b", "a");
        using var handler = new Handler(async message =>
        {
            using var document = JsonDocument.Parse(await message.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken));
            var root = document.RootElement;
            var messages = root.GetProperty("input");
            Assert.Equal(5, messages.GetArrayLength());
            Assert.Equal("system", messages[0].GetProperty("role").GetString());
            Assert.Equal("developer", messages[1].GetProperty("role").GetString());
            Assert.Equal("input_text", messages[0].GetProperty("content")[0].GetProperty("type").GetString());
            Assert.Equal("output_text", messages[3].GetProperty("content")[0].GetProperty("type").GetString());
            Assert.Equal(1, messages[2].GetProperty("content").GetArrayLength());
            var content = messages[4].GetProperty("content");
            Assert.Equal(4, content.GetArrayLength());
            Assert.Equal("question", content[0].GetProperty("text").GetString());
            Assert.Equal("input_image", content[1].GetProperty("type").GetString());
            Assert.Equal("https://example.test/a.png", content[1].GetProperty("image_url").GetString());
            Assert.Equal("high", content[1].GetProperty("detail").GetString());
            Assert.Equal("explicit", content[1].GetProperty("prompt_cache_breakpoint").GetProperty("mode").GetString());
            Assert.Equal("data:image/png;base64,AQ==", content[2].GetProperty("image_url").GetString());
            Assert.Equal(task.CreateOutputSpecification(new(), vocabulary), content[3].GetProperty("text").GetString());
            Assert.Equal("explicit", root.GetProperty("prompt_cache_options").GetProperty("mode").GetString());
            return Response(Envelope("{\"renamed\":\"xxx\",\"choice\":\"a\",\"items\":[null,null],\"number\":-3,\"optional\":null}"));
        });
        using var http = new HttpClient(handler);
        var result = await Client(http).ExecuteAsync(task, new()
        {
            Messages = [new(MessageRole.System, [new TextPart("system")]), new(MessageRole.Developer, [new TextPart("developer")]),
                new(MessageRole.User, [new TextPart("first")]), new(MessageRole.Assistant, [new TextPart("earlier")]),
                new(MessageRole.User, [new TextPart("question"), new ImagePart { Url = "https://example.test/a.png", Detail = ImageDetail.High, CacheBreakpoint = true }, new ImagePart { Url = "data:image/png;base64,AQ==" }])],
            Vocabularies = vocabulary,
            OutputSpecification = new(),
            OpenAi = new() { Cache = new() { Mode = OpenAiCacheMode.Explicit } }
        }, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
    }

    [Theory]
    [InlineData("both")]
    [InlineData("neither")]
    [InlineData("empty-messages")]
    [InlineData("empty-content")]
    [InlineData("role")]
    [InlineData("null-part")]
    [InlineData("empty-text")]
    [InlineData("detail")]
    [InlineData("http-image")]
    [InlineData("relative-image")]
    [InlineData("image-auth")]
    [InlineData("invalid-base64")]
    [InlineData("empty-base64")]
    [InlineData("unsupported-media")]
    [InlineData("assistant-image")]
    [InlineData("assistant-breakpoint")]
    [InlineData("guidance-no-user")]
    [InlineData("guidance-pattern")]
    [InlineData("text-vocabulary")]
    [InlineData("text-guidance")]
    public async Task UnsupportedInputsFailBeforeAuth(string scenario)
    {
        var auth = 0;
        using var handler = new Handler(_ => throw new InvalidOperationException("Should not send"));
        using var http = new HttpClient(handler);
        var request = new StructuredRequest { Messages = [new(MessageRole.User, [new TextPart("prompt")])], CredentialResolver = _ => { auth++; return ValueTask.FromResult<string?>("key"); } };
        request = scenario switch
        {
            "both" => request with { Input = "prompt" },
            "neither" => request with { Messages = null },
            "empty-messages" => request with { Messages = [] },
            "empty-content" => request with { Messages = [new(MessageRole.User, [])] },
            "role" => request with { Messages = [new((MessageRole)100, [new TextPart("prompt")])] },
            "null-part" => request with { Messages = [new(MessageRole.User, [null!])] },
            "empty-text" => request with { Messages = [new(MessageRole.User, [new TextPart(" ")])] },
            "assistant-image" => request with { Messages = [new(MessageRole.Assistant, [new ImagePart { Url = "https://example.test/a" }])] },
            "assistant-breakpoint" => request with { Messages = [new(MessageRole.Assistant, [new TextPart("x") { CacheBreakpoint = true }])] },
            "guidance-no-user" => request with { Messages = [new(MessageRole.Developer, [new TextPart("x")])], OutputSpecification = new() { IncludeExample = false } },
            "guidance-pattern" or "text-guidance" => request with { OutputSpecification = new() },
            "text-vocabulary" => request with { Vocabularies = Vocabulary("a") },
            _ => request with
            {
                Messages = [new(MessageRole.User, [new ImagePart { Detail = scenario == "detail" ? (ImageDetail)100 : ImageDetail.Auto,
                Url = scenario switch { "http-image" => "http://example.test/a", "relative-image" => "/a", "image-auth" => "https://user:pass@example.test/a",
                    "invalid-base64" => "data:image/png;base64,!!", "empty-base64" => "data:image/png;base64,", "unsupported-media" => "data:image/svg+xml;base64,AQ==", _ => "https://example.test/a" } }])]
            }
        };
        var error = scenario.StartsWith("text-", StringComparison.Ordinal) ? (await Client(http).GenerateTextAsync(new() { Request = request }, TestContext.Current.CancellationToken)).Error
            : (await Client(http).ExecuteAsync(Contract<Patterned>(), request, TestContext.Current.CancellationToken)).Error;
        Assert.Equal(StructuredErrorKind.InvalidRequest, error!.Kind);
        Assert.Equal(0, auth);
        Assert.Equal(0, handler.Calls);
    }

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
        var request = new StructuredRequest { Input = "prompt", OpenAi = options };
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
                OpenAi = new() { Cache = new() { Mode = scenario == "five-explicit" ? OpenAiCacheMode.Explicit : OpenAiCacheMode.Implicit } }
            },
            _ => request
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
        Assert.True((await Client(http, false).GenerateTextAsync(new() { Request = new() { Input = "prompt", OpenAi = new() { CacheRetention = retention } } }, TestContext.Current.CancellationToken)).IsSuccess);
    }

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
            Assert.False(root.TryGetProperty("max_output_tokens", out _));
            Assert.Equal(typed, root.TryGetProperty("text", out _));
            Assert.Equal(typed ? "Current instructions" : "Shared instructions", root.GetProperty("instructions").GetString());
            if (typed) Assert.Equal(Contract<Guided>().CreateSchema(Vocabulary("a")).GetRawText(), root.GetProperty("text").GetProperty("format").GetProperty("schema").GetRawText());
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
        var request = new StructuredRequest { Input = "prefix" };
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
            _ => request
        };
        var result = await Client(http, scenario != "legacy").PrewarmAsync(new() { Request = request }, TestContext.Current.CancellationToken);
        Assert.Equal(StructuredErrorKind.InvalidRequest, result.Error!.Kind);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task ContinuationUsesNewTypeSchemaAndVocabularyAndRetainsBilledFailure()
    {
        var calls = 0;
        using var handler = new Handler(async message =>
        {
            using var document = JsonDocument.Parse(await message.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken));
            var root = document.RootElement;
            Assert.Equal("Current instructions", root.GetProperty("instructions").GetString());
            var properties = root.GetProperty("text").GetProperty("format").GetProperty("schema").GetProperty("properties");
            if (++calls == 1) { Assert.True(root.GetProperty("store").GetBoolean()); Assert.True(properties.TryGetProperty("code", out _)); return Response(Envelope("{\"code\":\"AB\"}")); }
            Assert.Equal("current", root.GetProperty("previous_response_id").GetString());
            Assert.Equal("current", root.GetProperty("prompt_cache_options").GetProperty("comparison_response_id").GetString());
            Assert.False(root.GetProperty("store").GetBoolean());
            Assert.Equal("new", properties.GetProperty("choice").GetProperty("enum")[0].GetString());
            var envelope = JsonNode.Parse(Envelope("{\"renamed\":\"xxx\",\"choice\":\"old\",\"items\":[null,null],\"number\":-3,\"optional\":null}"))!;
            envelope["prompt_cache_diagnostics"]!["type"] = "cache_miss";
            envelope["prompt_cache_diagnostics"]!["reason"] = "text_format_changed";
            return Response(envelope.ToJsonString());
        });
        using var http = new HttpClient(handler);
        var client = Client(http);
        var first = await client.ExecuteAsync(Contract<Patterned>(), new() { Input = "first", OpenAi = new() { Store = true } }, TestContext.Current.CancellationToken);
        Assert.True(first.IsSuccess);
        var second = await client.ExecuteAsync(Contract<Guided>(), new()
        {
            Input = "followup",
            Vocabularies = Vocabulary("new"),
            OpenAi = new() { PreviousResponseId = first.Metadata.ResponseId, Cache = new() { ComparisonResponseId = first.Metadata.ResponseId } }
        }, TestContext.Current.CancellationToken);
        Assert.Equal(StructuredErrorKind.InvalidOutput, second.Error!.Kind);
        Assert.Equal(100, second.Metadata.Usage!.InputTokens);
        Assert.Equal("text_format_changed", second.Metadata.CacheDiagnostics!.Reason);
    }

    [Fact]
    public async Task ConcurrentGuidanceAndNullableEnumCollectionsKeepVocabulariesIndependent()
    {
        var bothSent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var handler = new Handler(async message =>
        {
            using var document = JsonDocument.Parse(await message.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken));
            var root = document.RootElement;
            var schema = root.GetProperty("text").GetProperty("format").GetProperty("schema");
            var choice = schema.GetProperty("properties").GetProperty("values").GetProperty("items").GetProperty("anyOf")[0].GetProperty("enum")[0].GetString();
            var guidance = root.GetProperty("input")[0].GetProperty("content")[1].GetProperty("text").GetString()!;
            Assert.Contains(JsonSerializer.Serialize(choice), guidance);
            Assert.DoesNotContain(JsonSerializer.Serialize(choice == "red" ? "blue" : "red"), guidance);
            if (Interlocked.Increment(ref calls) == 2) bothSent.SetResult();
            await bothSent.Task.WaitAsync(TestContext.Current.CancellationToken);
            return Response(Envelope(JsonSerializer.Serialize(new { values = new string?[] { null, choice }, enums = new string?[] { null, "yes" } })));
        });
        using var http = new HttpClient(handler);
        var client = Client(http);
        var task = Contract<NullableCollections>();
        var results = await Task.WhenAll(new[] { "red", "blue" }.Select(value => client.ExecuteAsync(task,
            new() { Input = "prompt", Vocabularies = Vocabulary(value), OutputSpecification = new() }, TestContext.Current.CancellationToken)));
        Assert.Equal("red", results[0].EnsureSuccess().Values[1]);
        Assert.Equal("blue", results[1].EnsureSuccess().Values[1]);
        Assert.All(results, result => Assert.Equal(Decision.Yes, result.EnsureSuccess().Enums[1]));
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
            Request = new()
            {
                Messages = [new(MessageRole.User, Enumerable.Range(0, count).Select(_ => (ContentPart)new TextPart("stable") { CacheBreakpoint = true }).ToArray())],
                OpenAi = new() { Cache = new() { Mode = mode } }
            }
        }, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
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

    [Fact]
    public async Task CallerMessagesAndMetadataAreSnapshottedBeforeCredentials()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var parts = new List<ContentPart> { new TextPart("original") };
        var messages = new List<StructuredMessage> { new(MessageRole.User, parts) };
        var metadata = new Dictionary<string, string> { ["job"] = "original" };
        using var handler = new Handler(async message =>
        {
            using var document = JsonDocument.Parse(await message.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.Equal("original", document.RootElement.GetProperty("input")[0].GetProperty("content")[0].GetProperty("text").GetString());
            Assert.Equal("original", document.RootElement.GetProperty("metadata").GetProperty("job").GetString());
            return Response(Envelope());
        });
        using var http = new HttpClient(handler);
        var pending = Client(http).GenerateTextAsync(new()
        {
            Request = new()
            {
                Messages = messages,
                OpenAi = new() { Metadata = metadata },
                CredentialResolver = _ => { entered.SetResult(); return new(release.Task); }
            }
        }, TestContext.Current.CancellationToken);
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        parts[0] = new TextPart("mutated");
        messages.Clear();
        metadata["job"] = "mutated";
        release.SetResult("key");
        Assert.True((await pending).IsSuccess);
    }

    public sealed record Guided
    {
        [JsonPropertyName("renamed"), Description("Explain this field"), StringConstraint(MinLength = 3, MaxLength = 4)]
        public required string Label { get; init; }
        [DynamicVocabulary("choices")]
        public required string Choice { get; init; }
        [CollectionConstraint(MinItems = 2, MaxItems = 3)]
        public required List<string?> Items { get; init; }
        [NumberConstraint(Minimum = -5, Maximum = -2)]
        public int Number { get; init; }
        public string? Optional { get; init; }
    }
    public sealed record Patterned
    {
        [StringConstraint(Pattern = "^[A-Z]{2}$")]
        public required string Code { get; init; }
    }
    public sealed record LargeSample
    {
        [CollectionConstraint(MinItems = 1000)]
        public required List<LargeSampleItem> Values { get; init; }
    }
    public sealed record LargeSampleItem
    {
        [CollectionConstraint(MinItems = 1000)]
        public required List<int> Values { get; init; }
    }
    public sealed record NullableCollections
    {
        [DynamicVocabulary("choices"), CollectionConstraint(MinItems = 1)]
        public required List<string?> Values { get; init; }
        public required List<Decision?> Enums { get; init; }
    }
    public enum Decision { [JsonStringEnumMemberName("yes")] Yes, No }
    sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        int _calls;
        public int Calls => Volatile.Read(ref _calls);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Interlocked.Increment(ref _calls); return send(request); }
    }
}
