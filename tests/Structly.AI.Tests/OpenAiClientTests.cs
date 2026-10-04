using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Structly.AI.OpenAI;

namespace Structly.AI.Tests;

public sealed class OpenAiClientTests
{
    static StructuredTask<Answer> TaskContract(StructuredTaskOptions? options = null) => StructuredTask.Create<Answer>(options ?? new() { Instructions = "Extract", SchemaName = "answer", Description = "Answer" });
    static OpenAiClient Client(HttpClient http, OpenAiClientOptions? options = null) => new(http, options ?? new()
    { DefaultModel = new() { ModelId = "configured" }, CredentialResolver = Credentials.FromStatic("client-key") });
    static string Envelope(string status = "completed", string text = "{\"value\":42}", object[]? output = null, object? usage = null, string? code = null) => JsonSerializer.Serialize(new
    {
        id = "resp-1",
        model = "resolved",
        status,
        error = code is null ? null : new { code },
        usage = usage ?? new { input_tokens = 10, output_tokens = 5, total_tokens = 15, input_tokens_details = new { cached_tokens = 2 }, output_tokens_details = new { reasoning_tokens = 1 } },
        output = output ?? [new { type = "message", role = "assistant", status = "completed", content = new[] { new { type = "output_text", text } } }]
    });
    static HttpResponseMessage Response(string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent(body) };
        response.Headers.Add("x-request-id", "request-1");
        return response;
    }

    [Fact]
    public async Task WireContractAndCallerTransportArePreserved()
    {
        using var handler = new Handler(async message =>
        {
            Assert.Equal(HttpMethod.Post, message.Method);
            Assert.Equal("https://example.test/api/responses", message.RequestUri!.AbsoluteUri);
            Assert.Equal("Bearer", message.Headers.Authorization!.Scheme);
            Assert.Equal("request-key", message.Headers.Authorization.Parameter);
            Assert.Equal("idem", Assert.Single(message.Headers.GetValues("Idempotency-Key")));
            Assert.Equal("application/json", message.Content!.Headers.ContentType!.MediaType);
            using var doc = JsonDocument.Parse(await message.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            var root = doc.RootElement;
            Assert.Equal("selected", root.GetProperty("model").GetString());
            Assert.Equal("high", root.GetProperty("reasoning").GetProperty("effort").GetString());
            Assert.Equal("Extract", root.GetProperty("instructions").GetString());
            Assert.Equal("input", root.GetProperty("input").GetString());
            Assert.False(root.GetProperty("store").GetBoolean());
            Assert.False(root.GetProperty("stream").GetBoolean());
            Assert.Equal(100, root.GetProperty("max_output_tokens").GetInt32());
            Assert.Equal("job", root.GetProperty("metadata").GetProperty("purpose").GetString());
            var format = root.GetProperty("text").GetProperty("format");
            Assert.Equal("json_schema", format.GetProperty("type").GetString());
            Assert.True(format.GetProperty("strict").GetBoolean());
            Assert.Equal("answer", format.GetProperty("name").GetString());
            Assert.Equal("Answer", format.GetProperty("description").GetString());
            Assert.Equal(TaskContract().CreateSchema().GetRawText(), format.GetProperty("schema").GetRawText());
            return Response(Envelope());
        });
        using var http = new HttpClient(handler) { BaseAddress = new("https://unused.test/"), Timeout = TimeSpan.FromSeconds(7) };
        http.DefaultRequestHeaders.Authorization = new("Bearer", "shared");
        var client = Client(http, new() { DefaultModel = new() { ModelId = "default" }, BaseAddress = new("https://example.test/api/") });
        var request = new StructuredRequest
        {
            Input = "input",
            CorrelationId = "local",
            MaxOutputTokens = 100,
            ModelSelection = new() { ModelId = "selected", ReasoningEffort = ReasoningEffort.High },
            CredentialResolver = Credentials.FromStatic("request-key"),
            OpenAi = new() { IdempotencyKey = "idem", Metadata = new Dictionary<string, string> { ["purpose"] = "job" }, CaptureOutputText = true, CaptureRawResponse = true }
        };
        var result = await client.ExecuteAsync(TaskContract(), request, TestContext.Current.CancellationToken);
        Assert.Equal(42, result.EnsureSuccess().Value);
        Assert.Equal("resp-1", result.Metadata.ResponseId);
        Assert.Equal("resolved", result.Metadata.ResolvedModel);
        Assert.Equal("selected", result.Metadata.RequestedModel);
        Assert.Equal("request-1", result.Metadata.ProviderRequestId);
        Assert.Equal("local", result.Metadata.CorrelationId);
        Assert.Equal(15, result.Metadata.Usage!.TotalTokens);
        Assert.Equal(2, result.Metadata.Usage.CachedInputTokens);
        Assert.Equal(1, result.Metadata.Usage.ReasoningTokens);
        Assert.Equal("{\"value\":42}", result.Metadata.OutputText);
        Assert.Equal("resp-1", result.Metadata.RawResponse!.Value.GetProperty("id").GetString());
        Assert.Equal("shared", http.DefaultRequestHeaders.Authorization.Parameter);
        Assert.Equal(TimeSpan.FromSeconds(7), http.Timeout);
        Assert.Equal("https://unused.test/", http.BaseAddress.AbsoluteUri);
        Assert.False(handler.Disposed);
    }

    [Theory]
    [InlineData("incomplete", StructuredErrorKind.IncompleteOutput)]
    [InlineData("queued", StructuredErrorKind.InvalidResponse)]
    [InlineData("in_progress", StructuredErrorKind.InvalidResponse)]
    [InlineData("failed", StructuredErrorKind.ProviderRejected)]
    public async Task StatusWinsOverValidTypedText(string status, StructuredErrorKind expected)
    {
        using var handler = new Handler(_ => System.Threading.Tasks.Task.FromResult(Response(Envelope(status))));
        using var http = new HttpClient(handler);
        var result = await Client(http).ExecuteAsync(TaskContract(), new() { Input = "input" }, TestContext.Current.CancellationToken);
        Assert.Equal(expected, result.Error!.Kind);
        Assert.Equal(15, result.Metadata.Usage!.TotalTokens);
        Assert.Equal("resp-1", result.Metadata.ResponseId);
        Assert.Null(result.Metadata.RawResponse);
        Assert.Null(result.Metadata.OutputText);
    }

    [Theory]
    [InlineData("refusal", StructuredErrorKind.Refused)]
    [InlineData("two", StructuredErrorKind.InvalidResponse)]
    [InlineData("unexpected", StructuredErrorKind.InvalidResponse)]
    [InlineData("empty", StructuredErrorKind.InvalidResponse)]
    [InlineData("bad-json", StructuredErrorKind.InvalidOutput)]
    [InlineData("bad-dto", StructuredErrorKind.InvalidOutput)]
    public async Task OutputFailuresRetainBilling(string scenario, StructuredErrorKind expected)
    {
        object message = new { type = "message", role = "assistant", status = "completed", content = new[] { new { type = "output_text", text = "{\"value\":42}" } } };
        var body = scenario switch
        {
            "refusal" => Envelope(output: [new { type = "message", role = "assistant", status = "completed", content = new object[] { new { type = "refusal", refusal = "sensitive" }, new { type = "output_text", text = "{}" } } }]),
            "two" => Envelope(output: [message, message]),
            "unexpected" => Envelope(output: [new { type = "tool_call" }]),
            "empty" => Envelope(output: []),
            "bad-json" => Envelope(text: "secret not JSON"),
            _ => Envelope(text: "{\"value\":\"secret\"}")
        };
        using var handler = new Handler(_ => System.Threading.Tasks.Task.FromResult(Response(body)));
        using var http = new HttpClient(handler);
        var result = await Client(http).ExecuteAsync(TaskContract(), new() { Input = "input" }, TestContext.Current.CancellationToken);
        Assert.Equal(expected, result.Error!.Kind);
        Assert.Equal("resolved", result.Metadata.ResolvedModel);
        Assert.Equal(10, result.Metadata.Usage!.InputTokens);
        Assert.DoesNotContain("secret", result.Error.Message);
        Assert.DoesNotContain("sensitive", result.Error.Message);
    }

    [Theory]
    [InlineData(401, StructuredErrorKind.Authentication, false)]
    [InlineData(403, StructuredErrorKind.PermissionDenied, false)]
    [InlineData(429, StructuredErrorKind.RateLimited, true)]
    [InlineData(408, StructuredErrorKind.ProviderUnavailable, true)]
    [InlineData(503, StructuredErrorKind.ProviderUnavailable, true)]
    [InlineData(400, StructuredErrorKind.ProviderRejected, false)]
    public async Task HttpFailuresAreSanitizedAndOneAttempt(int status, StructuredErrorKind expected, bool transient)
    {
        using var handler = new Handler(_ =>
        {
            var response = Response("{\"error\":{\"message\":\"secret\"}}", (HttpStatusCode)status);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(8));
            return System.Threading.Tasks.Task.FromResult(response);
        });
        using var http = new HttpClient(handler);
        var result = await Client(http).ExecuteAsync(TaskContract(), new() { Input = "input" }, TestContext.Current.CancellationToken);
        Assert.Equal(expected, result.Error!.Kind);
        Assert.Equal(transient, result.Error.IsTransient);
        Assert.Equal((HttpStatusCode)status, result.Error.HttpStatusCode);
        Assert.Equal(TimeSpan.FromSeconds(8), result.Error.RetryAfter);
        Assert.DoesNotContain("secret", result.Error.Message);
        Assert.Equal("request-1", result.Metadata.ProviderRequestId);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task QuotaAndProviderTransientCodesAreClassified()
    {
        using var handler = new Handler(_ => System.Threading.Tasks.Task.FromResult(Response("{\"error\":{\"code\":\"insufficient_quota\"}}", HttpStatusCode.TooManyRequests)));
        using var http = new HttpClient(handler);
        Assert.False((await Client(http).ExecuteAsync(TaskContract(), new() { Input = "input" }, TestContext.Current.CancellationToken)).Error!.IsTransient);
        handler.Callback = _ => System.Threading.Tasks.Task.FromResult(Response(Envelope("failed", code: "server_error")));
        var result = await Client(http).ExecuteAsync(TaskContract(), new() { Input = "input" }, TestContext.Current.CancellationToken);
        Assert.Equal(StructuredErrorKind.ProviderUnavailable, result.Error!.Kind);
        Assert.True(result.Error.IsTransient);
        Assert.NotNull(result.Metadata.Usage);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("null")]
    public async Task MalformedEnvelopesAreResults(string body)
    {
        using var handler = new Handler(_ => System.Threading.Tasks.Task.FromResult(Response(body)));
        using var http = new HttpClient(handler);
        Assert.Equal(StructuredErrorKind.InvalidResponse, (await Client(http).ExecuteAsync(TaskContract(), new() { Input = "input" }, TestContext.Current.CancellationToken)).Error!.Kind);
    }

    [Fact]
    public async Task InvalidUsageDoesNotDiscardIndependentCountsOrOutput()
    {
        using var handler = new Handler(_ => System.Threading.Tasks.Task.FromResult(Response(Envelope(usage: new { input_tokens = -1, output_tokens = 5, total_tokens = "bad" }))));
        using var http = new HttpClient(handler);
        var result = await Client(http).ExecuteAsync(TaskContract(), new() { Input = "input" }, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Null(result.Metadata.Usage!.InputTokens);
        Assert.Equal(5, result.Metadata.Usage.OutputTokens);
        Assert.Equal(2, result.Warnings.Count);
    }

    [Fact]
    public async Task LocalValidationSuppressesCredentialResolutionAndHttp()
    {
        using var handler = new Handler(_ => throw new InvalidOperationException("Should not send"));
        using var http = new HttpClient(handler);
        var resolved = 0;
        var client = Client(http, new() { DefaultModel = new() { ModelId = "configured" }, CredentialResolver = _ => { resolved++; return ValueTask.FromResult<string?>("key"); } });
        StructuredRequest[] invalid = [new() { Input = " " }, new() { Input = "input", MaxOutputTokens = 0 },
            new() { Input = "input", ModelSelection = new() { ProfileName = "unknown" } },
            new() { Input = "input", ModelSelection = new() { ModelId = "a", ProfileName = "b" } },
            new() { Input = "input", OpenAi = new() { IdempotencyKey = "bad\r\nheader" } },
            new() { Input = "input", OpenAi = new() { Metadata = new Dictionary<string, string> { ["k"] = new string('x', 513) } } }];
        foreach (var request in invalid) Assert.Equal(StructuredErrorKind.InvalidRequest, (await client.ExecuteAsync(TaskContract(), request, TestContext.Current.CancellationToken)).Error!.Kind);
        var dynamicTask = StructuredTask.Create<DynamicAnswer>(new() { Instructions = "Extract" });
        Assert.Equal(StructuredErrorKind.UnsupportedSchema, (await client.ExecuteAsync(dynamicTask, new() { Input = "input" }, TestContext.Current.CancellationToken)).Error!.Kind);
        Assert.Equal(0, resolved);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task CredentialsAndModelsFollowPrecedenceWithoutFallback()
    {
        var keys = new List<string>();
        var models = new List<string>();
        using var handler = new Handler(async message =>
        {
            keys.Add(message.Headers.Authorization!.Parameter!);
            using var document = JsonDocument.Parse(await message.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken));
            models.Add(document.RootElement.GetProperty("model").GetString()!);
            return Response(Envelope());
        });
        using var http = new HttpClient(handler);
        var profiles = new Dictionary<string, ModelSelection> { ["task"] = new() { ModelId = "task-model", ReasoningEffort = ReasoningEffort.Low } };
        var client = Client(http, new() { DefaultModel = new() { ModelId = "client-model" }, CredentialResolver = Credentials.FromStatic("client"), Profiles = profiles });
        profiles.Clear();
        var task = TaskContract(new() { Instructions = "Extract", CredentialResolver = Credentials.FromStatic("task"), ModelSelection = new() { ProfileName = "task" } });
        await client.ExecuteAsync(TaskContract(), new() { Input = "input" }, TestContext.Current.CancellationToken);
        await client.ExecuteAsync(task, new() { Input = "input" }, TestContext.Current.CancellationToken);
        await client.ExecuteAsync(task, new() { Input = "input", ModelSelection = new() { ModelId = "request-model" }, CredentialResolver = Credentials.FromStatic("request") }, TestContext.Current.CancellationToken);
        Assert.Equal(new[] { "client", "task", "request" }, keys);
        Assert.Equal(new[] { "client-model", "task-model", "request-model" }, models);
        Assert.Equal(StructuredErrorKind.Authentication, (await client.ExecuteAsync(task, new() { Input = "input", CredentialResolver = _ => ValueTask.FromResult<string?>(null) }, TestContext.Current.CancellationToken)).Error!.Kind);
        Assert.Equal(StructuredErrorKind.Authentication, (await client.ExecuteAsync(task, new() { Input = "input", CredentialResolver = _ => throw new InvalidOperationException("secret") }, TestContext.Current.CancellationToken)).Error!.Kind);
        Assert.Equal(3, handler.Calls);
    }

    [Fact]
    public async Task ConcurrentRequestCredentialsRemainIsolated()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new Handler(async message =>
        {
            var key = message.Headers.Authorization!.Parameter;
            if (key == "first") { entered.SetResult(); await release.Task; }
            else release.SetResult();
            Assert.Equal(key, message.Headers.Authorization.Parameter);
            return Response(Envelope(text: key == "first" ? "{\"value\":1}" : "{\"value\":2}"));
        });
        using var http = new HttpClient(handler);
        var client = Client(http);
        var task = TaskContract();
        var first = client.ExecuteAsync(task, new() { Input = "input", CredentialResolver = Credentials.FromStatic("first") }, TestContext.Current.CancellationToken);
        await entered.Task;
        var second = client.ExecuteAsync(task, new() { Input = "input", CredentialResolver = Credentials.FromStatic("second") }, TestContext.Current.CancellationToken);
        Assert.Equal(1, (await first).EnsureSuccess().Value);
        Assert.Equal(2, (await second).EnsureSuccess().Value);
        Assert.Null(http.DefaultRequestHeaders.Authorization);
    }

    [Fact]
    public async Task EnvironmentFactoryRereadsOnlyExplicitVariable()
    {
        var variable = "STRUCTLY_TEST_" + Guid.NewGuid().ToString("N");
        try
        {
            var resolver = Credentials.FromEnvironment(variable);
            Assert.Null(await resolver(TestContext.Current.CancellationToken));
            Environment.SetEnvironmentVariable(variable, "first");
            Assert.Equal("first", await resolver(TestContext.Current.CancellationToken));
            Environment.SetEnvironmentVariable(variable, "second");
            Assert.Equal("second", await resolver(TestContext.Current.CancellationToken));
        }
        finally { Environment.SetEnvironmentVariable(variable, null); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TransportExceptionsAreSafe(bool cancelled)
    {
        using var handler = new Handler(_ => cancelled ? throw new OperationCanceledException("secret") : throw new HttpRequestException("secret"));
        using var http = new HttpClient(handler);
        var result = await Client(http).ExecuteAsync(TaskContract(), new() { Input = "input" }, TestContext.Current.CancellationToken);
        Assert.Equal(StructuredErrorKind.TransportFailure, result.Error!.Kind);
        Assert.True(result.Error.IsTransient);
        Assert.DoesNotContain("secret", result.Error.Message);
    }

    [Fact]
    public async Task ResponseCeilingAndPrecancelAreEnforced()
    {
        using var handler = new Handler(_ => System.Threading.Tasks.Task.FromResult(Response(Envelope())));
        using var http = new HttpClient(handler);
        var client = Client(http, new() { DefaultModel = new() { ModelId = "configured" }, CredentialResolver = Credentials.FromStatic("key"), MaxResponseBytes = 20 });
        Assert.Equal(StructuredErrorKind.InvalidResponse, (await client.ExecuteAsync(TaskContract(), new() { Input = "input" }, TestContext.Current.CancellationToken)).Error!.Kind);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ExecuteAsync(TaskContract(), new() { Input = "input" }, cancelled.Token));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task TextPartsAreJoinedAndReasoningIsIgnored()
    {
        using var handler = new Handler(_ => System.Threading.Tasks.Task.FromResult(Response(Envelope(output:
        [new { type = "reasoning", summary = new[] { new { text = "sensitive" } } },
         new { type = "message", role = "assistant", status = "completed", content = new[]
         { new { type = "output_text", text = "{\"value\":" }, new { type = "output_text", text = "42}" } } }]))));
        using var http = new HttpClient(handler);
        var result = await Client(http).ExecuteAsync(TaskContract(), new() { Input = "input" }, TestContext.Current.CancellationToken);
        Assert.Equal(42, result.EnsureSuccess().Value);
        Assert.Null(result.Metadata.OutputText);
    }

    [Fact]
    public async Task RuntimeVocabularyAndConstraintsReachTheWireAndValidateOutput()
    {
        using var handler = new Handler(async message =>
        {
            using var document = JsonDocument.Parse(await message.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken));
            var property = document.RootElement.GetProperty("text").GetProperty("format").GetProperty("schema").GetProperty("properties").GetProperty("value");
            Assert.Equal("allowed", property.GetProperty("enum")[0].GetString());
            Assert.Equal(2, property.GetProperty("minLength").GetInt32());
            return Response(Envelope(text: "{\"value\":\"other\"}"));
        });
        using var http = new HttpClient(handler);
        var task = StructuredTask.Create<ConstrainedAnswer>(new() { Instructions = "Extract" });
        var result = await Client(http).ExecuteAsync(task, new() { Input = "input", Vocabularies = new Dictionary<string, IReadOnlyList<string>> { ["values"] = new[] { "allowed" } } }, TestContext.Current.CancellationToken);
        Assert.Equal(StructuredErrorKind.InvalidOutput, result.Error!.Kind);
        Assert.Equal(15, result.Metadata.Usage!.TotalTokens);
    }

    [Fact]
    public async Task ResponseResourcesAreDisposedOnOutputFailure()
    {
        var content = new TrackedContent(Envelope(text: "invalid"));
        using var handler = new Handler(_ => System.Threading.Tasks.Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content }));
        using var http = new HttpClient(handler);
        var result = await Client(http).ExecuteAsync(TaskContract(), new() { Input = "input" }, TestContext.Current.CancellationToken);
        Assert.Equal(StructuredErrorKind.InvalidOutput, result.Error!.Kind);
        Assert.True(content.Disposed);
        Assert.False(handler.Disposed);
    }

    [Fact]
    public void InvalidConfigurationFailsWithoutTransport()
    {
        using var http = new HttpClient();
        Assert.Throws<ArgumentException>(() => Client(http, new() { DefaultModel = new() { ModelId = " " } }));
        Assert.Throws<ArgumentException>(() => Client(http, new() { DefaultModel = new() { ProfileName = "missing" } }));
        Assert.Throws<ArgumentException>(() => Client(http, new() { DefaultModel = new() { ModelId = "model" }, BaseAddress = new("https://example.test/v1") }));
        Assert.Throws<ArgumentException>(() => TaskContract(new() { Instructions = "Extract", ModelSelection = new() { ModelId = "a", ProfileName = "b" } }));
    }

    public sealed class ConstrainedAnswer
    {
        [DynamicVocabulary("values"), StringConstraint(MinLength = 2)]
        public string Value { get; init; } = "";
    }
    sealed class TrackedContent(string text) : StringContent(text)
    {
        public bool Disposed { get; set; }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }

    [Fact]
    public async Task IncompleteOutputCaptureRetainsPartialTextAndRawEnvelope()
    {
        using var handler = new Handler(_ => System.Threading.Tasks.Task.FromResult(Response(Envelope("incomplete", text: "partial"))));
        using var http = new HttpClient(handler);
        var result = await Client(http).ExecuteAsync(TaskContract(), new() { Input = "input", OpenAi = new() { CaptureRawResponse = true, CaptureOutputText = true } }, TestContext.Current.CancellationToken);
        Assert.Equal(StructuredErrorKind.IncompleteOutput, result.Error!.Kind);
        Assert.Equal("partial", result.Metadata.OutputText);
        Assert.Equal("incomplete", result.Metadata.RawResponse!.Value.GetProperty("status").GetString());
    }

    public sealed class Answer { public int Value { get; init; } }
    public sealed class DynamicAnswer { [DynamicVocabulary("values")] public string Value { get; init; } = ""; }
    sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> callback) : HttpMessageHandler
    {
        public Func<HttpRequestMessage, Task<HttpResponseMessage>> Callback { get; set; } = callback;
        public int Calls { get; set; }
        public bool Disposed { get; set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Calls++; return Callback(request); }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
}
