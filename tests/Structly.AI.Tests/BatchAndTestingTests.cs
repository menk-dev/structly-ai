using System.Net;
using System.Text;
using System.Text.Json;
using Structly.AI.Embeddings;
using Structly.AI.OpenAI;
using Structly.AI.Testing;

namespace Structly.AI.Tests;

public sealed class BatchAndTestingTests
{
    static OpenAiClient Client(HttpClient http, Func<StructuredUsageEvent, CancellationToken, ValueTask>? observer = null) => new(http, new() { DefaultModel = new() { ModelId = "gpt-6-sol" }, CredentialResolver = Credentials.FromStatic("key"), UsageObserver = observer });
    static EmbeddingBatchItem Item(string id) => new(id, new() { Inputs = ["input"], ModelSelection = new() { ModelId = "embedding" }, Dimensions = 2 });
    static HttpResponseMessage Response(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value)) };
    static object Job(string status = "completed", string endpoint = "/v1/embeddings") => new { id = "batch", status, endpoint, input_file_id = "input", output_file_id = "output", error_file_id = "errors" };
    static string Line(string id, JsonElement body) => JsonSerializer.Serialize(new { custom_id = id, response = new { status_code = 200, body } }) + "\n";
    static JsonElement Vectors() => ResponseEnvelopes.Embeddings([new float[] { 1, 2 }], "embedding", new() { InputTokens = 7 });

    [Fact]
    public async Task RestartImportOrdersResultsAndReusesAccountingIds()
    {
        using var handler = new Handler(request => request.RequestUri!.AbsolutePath switch
        {
            "/v1/batches/batch" => Response(Job("cancelled")),
            "/v1/files/output/content" => new(HttpStatusCode.OK) { Content = new StringContent(Line("b", Vectors()) + Line("a", Vectors())) },
            _ => new(HttpStatusCode.OK) { Content = new StringContent("") }
        });
        using var http = new HttpClient(handler);
        var observed = new List<Guid>();
        var client = Client(http, (item, _) => { Assert.Equal("embedding", item.RequestedModel); Assert.Equal(7, item.Usage.InputTokens); observed.Add(item.Metadata.ExecutionId); return ValueTask.CompletedTask; });
        var prepared = client.PrepareEmbeddingBatch([Item("a"), Item("b"), Item("missing")]);
        var persisted = BatchManifest.FromJson(prepared.Manifest.ToJson());
        for (var i = 0; i < 2; i++)
        {
            var results = (await client.ImportEmbeddingBatchResultsAsync("batch", persisted, cancellationToken: TestContext.Current.CancellationToken)).EnsureSuccess();
            Assert.Equal("a", results[0].Metadata.BatchCustomId); Assert.True(results[0].IsSuccess);
            Assert.Equal(new float[] { 1, 2 }, results[1].Value![0]);
            Assert.Equal("BatchResultMissing", Assert.Single(results[2].Error!.Issues).Code);
            Assert.Equal(persisted.Items[0].ExecutionId, results[0].Metadata.ExecutionId);
        }
        Assert.Equal(4, observed.Count); Assert.Equal(observed[0], observed[2]);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("duplicate")]
    [InlineData("limit")]
    public async Task MalformedImportFailsOuterResultWithAlreadyObservedUsage(string scenario)
    {
        using var handler = new Handler(request => request.RequestUri!.AbsolutePath.EndsWith("batch", StringComparison.Ordinal) ? Response(Job()) :
            new(HttpStatusCode.OK) { Content = new StringContent(Line("a", Vectors()) + Line(scenario == "unknown" ? "other" : "a", Vectors())) });
        using var http = new HttpClient(handler); var events = 0;
        var client = Client(http, (_, _) => { events++; return ValueTask.CompletedTask; });
        var prepared = client.PrepareEmbeddingBatch([Item("a")]);
        var result = await client.ImportEmbeddingBatchResultsAsync("batch", prepared.Manifest, new() { MaxDownloadBytes = scenario == "limit" ? 1 : 10000 }, TestContext.Current.CancellationToken);
        Assert.Equal(StructuredErrorKind.InvalidResponse, result.Error!.Kind);
        Assert.Equal(scenario == "limit" ? 0 : 1, events);
    }

    [Fact]
    public async Task TypedImportValidatesFingerprintBeforeHttpAndRetainsInvalidOutputUsage()
    {
        var task = StructuredTask.Create<Answer>(new() { Instructions = "Extract" });
        var envelope = ResponseEnvelopes.CompletedText("{\"value\":42}", "resolved", usage: new() { TotalTokens = 8 });
        using var handler = new Handler(request => request.RequestUri!.AbsolutePath.EndsWith("batch", StringComparison.Ordinal) ? Response(Job(endpoint: "/v1/responses")) : new(HttpStatusCode.OK) { Content = new StringContent(request.RequestUri.AbsolutePath.Contains("errors", StringComparison.Ordinal) ? "" : Line("a", envelope)) });
        using var http = new HttpClient(handler); StructuredUsageEvent? observed = null;
        var client = Client(http, (item, _) => { observed = item; return ValueTask.CompletedTask; });
        var prepared = client.PrepareResponseBatch(task, [new("a", new() { Input = "input" })]);
        var wrong = StructuredTask.Create<OtherAnswer>(new() { Instructions = "Extract" });
        Assert.Equal(StructuredErrorKind.InvalidRequest, (await client.ImportResponseBatchResultsAsync("batch", prepared.Manifest, wrong, cancellationToken: TestContext.Current.CancellationToken)).Error!.Kind);
        Assert.Equal(0, handler.Calls);
        var results = (await client.ImportResponseBatchResultsAsync("batch", prepared.Manifest, task, cancellationToken: TestContext.Current.CancellationToken)).EnsureSuccess();
        Assert.Equal(StructuredErrorKind.InvalidOutput, results[0].Error!.Kind); Assert.Equal(8, observed!.Usage.TotalTokens);
    }

    [Fact]
    public async Task MultipartSubmissionRetainsFileOnCreateFailureAndLeavesCallerStreamsOpen()
    {
        using var handler = new AsyncHandler(async request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("files", StringComparison.Ordinal))
            {
                Assert.IsType<MultipartFormDataContent>(request.Content);
                var body = await request.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken);
                Assert.Contains("batch", body); Assert.Contains("custom_id", body);
                return Response(new { id = "uploaded", filename = "batch.jsonl", bytes = 20, purpose = "batch" });
            }
            return new(HttpStatusCode.BadRequest) { Content = new StringContent("{}") };
        });
        using var http = new HttpClient(handler); var client = Client(http); var prepared = client.PrepareEmbeddingBatch([Item("a")]);
        using var source = new MemoryStream(Encoding.UTF8.GetBytes(prepared.Jsonl));
        Assert.True((await client.UploadBatchFileAsync(source, cancellationToken: TestContext.Current.CancellationToken)).IsSuccess); Assert.True(source.CanRead);
        var result = await client.SubmitBatchAsync(prepared, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("uploaded", result.Metadata.UploadedFileId); Assert.Equal(400, result.Error!.HttpStatusCodeValue);
    }

    [Fact]
    public async Task LifecycleParsingAndDownloadOwnership()
    {
        using var handler = new Handler(request => request.RequestUri!.AbsolutePath switch
        {
            "/v1/files/f/content" => new(HttpStatusCode.OK) { Content = new StringContent("content") },
            "/v1/files/f" when request.Method == HttpMethod.Delete => Response(new { deleted = true }),
            "/v1/files/f" => Response(new { id = "f", filename = "batch.jsonl", bytes = 12 }),
            "/v1/batches" when request.Method == HttpMethod.Get => Response(new { data = new[] { Job() }, has_more = true, last_id = "batch" }),
            _ => Response(Job("cancelling"))
        });
        using var http = new HttpClient(handler); var client = Client(http); var token = TestContext.Current.CancellationToken;
        Assert.Equal(12, (await client.GetFileAsync("f", cancellationToken: token)).EnsureSuccess().Bytes);
        Assert.True((await client.DeleteFileAsync("f", cancellationToken: token)).EnsureSuccess());
        using var destination = new MemoryStream(); Assert.True((await client.DownloadFileContentAsync("f", destination, cancellationToken: token)).IsSuccess); Assert.True(destination.CanWrite);
        Assert.Equal("content", Encoding.UTF8.GetString(destination.ToArray()));
        Assert.True((await client.ListBatchesAsync(cancellationToken: token)).EnsureSuccess().HasMore);
        Assert.Equal("cancelling", (await client.CancelBatchAsync("batch", cancellationToken: token)).EnsureSuccess().Status);
    }

    [Fact]
    public void PreparationRejectsDuplicateModelsAndTransportControls()
    {
        using var http = new HttpClient(); var client = Client(http);
        Assert.Throws<ArgumentException>(() => client.PrepareEmbeddingBatch([Item("a"), Item("a")]));
        Assert.Throws<ArgumentException>(() => client.PrepareEmbeddingBatch([Item("a"), new("b", Item("b").Request with { ModelSelection = new() { ModelId = "other" } })]));
        Assert.Throws<ArgumentException>(() => client.PrepareEmbeddingBatch([new("a", Item("a").Request with { TotalTimeout = TimeSpan.FromSeconds(1) })]));
    }

    [Fact]
    public async Task TestingBuildersPreserveInvalidJsonAndExplicitCompletion()
    {
        var task = StructuredTask.Create<Answer>(new());
        Assert.True(task.ReadOutput(task.CreateExample().GetRawText()).IsSuccess);
        var completed = ResponseEnvelopes.CompleteExample(task, "{}"); Assert.True(task.ReadOutput(completed.GetRawText()).IsSuccess);
        Assert.Equal("supplied", ResponseEnvelopes.CompleteExample(task, "{\"value\":\"supplied\"}").GetProperty("value").GetString());
        Assert.Throws<ArgumentException>(() => ResponseEnvelopes.CompleteExample(task, "{\"value\":null}"));
        Assert.Throws<ArgumentException>(() => ResponseEnvelopes.CompleteExample(task, "{\"unknown\":true}"));
        using var handler = new Handler(_ => ResponseEnvelopes.ToHttpResponse(ResponseEnvelopes.CompletedText("{broken")));
        using var http = new HttpClient(handler);
        var result = await Client(http).ExecuteAsync(task, new() { Input = "input", Instructions = "Extract" }, TestContext.Current.CancellationToken);
        Assert.Equal(StructuredErrorKind.InvalidOutput, result.Error!.Kind);
    }

    [Fact]
    public async Task BatchTransportDeadlineAndCallerCancellationDisposeLateResponses()
    {
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new AsyncHandler(_ => pending.Task);
        using var http = new HttpClient(handler);
        var clock = new ManualClock();
        var client = new OpenAiClient(http, new() { DefaultModel = new() { ModelId = "model" }, CredentialResolver = Credentials.FromStatic("key"), TimeProvider = clock });
        var resultTask = client.GetBatchAsync("batch", new() { TotalTimeout = TimeSpan.FromSeconds(2) }, TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromSeconds(2));
        var result = await resultTask;
        Assert.Equal(StructuredErrorKind.DeadlineExceeded, result.Error!.Kind);
        Assert.Equal(TimeSpan.FromSeconds(2), result.Error.TotalTimeout);
        var content = new TrackedContent(); pending.SetResult(new(HttpStatusCode.OK) { Content = content });
        await content.Disposed.Task.WaitAsync(TestContext.Current.CancellationToken);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var exception = await Assert.ThrowsAsync<StructuredOperationCanceledException>(() => client.GetBatchAsync("batch", cancellationToken: cancellation.Token));
        Assert.Equal(cancellation.Token, exception.CancellationToken);
    }

    [Fact]
    public async Task ErrorFilesAreImportedWithoutInventingMissingUsageAndObserversAreBestEffort()
    {
        var errorLine = JsonSerializer.Serialize(new { custom_id = "b", error = new { code = "batch_expired", message = "private-provider-message" } }) + "\n";
        using var handler = new Handler(request => request.RequestUri!.AbsolutePath.EndsWith("batch", StringComparison.Ordinal) ? Response(Job("expired")) : new(HttpStatusCode.OK) { Content = new StringContent(request.RequestUri.AbsolutePath.Contains("errors", StringComparison.Ordinal) ? errorLine : Line("a", Vectors())) });
        using var http = new HttpClient(handler); var client = Client(http);
        var prepared = client.PrepareEmbeddingBatch([Item("a"), Item("b")]); var events = 0;
        var result = await client.ImportEmbeddingBatchResultsAsync("batch", prepared.Manifest, new() { UsageObserver = (_, _) => { events++; return ValueTask.FromException(new InvalidOperationException("private-observer-message")); } }, TestContext.Current.CancellationToken);
        var items = result.EnsureSuccess(); Assert.True(items[0].IsSuccess); Assert.Equal(StructuredErrorKind.BatchItemFailed, items[1].Error!.Kind);
        Assert.Null(items[1].Metadata.Usage); Assert.Equal(1, events); Assert.Contains(result.Warnings, warning => warning.Code == "UsageObserverFailed");
        Assert.DoesNotContain("private", items[1].Error!.Message);
    }

    [Fact]
    public async Task NonterminalImportsAndPerEnvelopeLimitsFailBeforeProcessingItems()
    {
        using var handler = new Handler(request => request.RequestUri!.AbsolutePath.EndsWith("batch", StringComparison.Ordinal) ? Response(Job("in_progress")) : throw new InvalidOperationException("Do not download"));
        using var http = new HttpClient(handler); var client = Client(http); var manifest = client.PrepareEmbeddingBatch([Item("a")]).Manifest;
        Assert.Equal(StructuredErrorKind.InvalidRequest, (await client.ImportEmbeddingBatchResultsAsync("batch", manifest, cancellationToken: TestContext.Current.CancellationToken)).Error!.Kind);
        Assert.Equal(1, handler.Calls);
        using var limitedHandler = new Handler(request => request.RequestUri!.AbsolutePath.EndsWith("batch", StringComparison.Ordinal) ? Response(Job()) : new(HttpStatusCode.OK) { Content = new StringContent(new string('x', 2048)) });
        using var limitedHttp = new HttpClient(limitedHandler);
        var limitedClient = new OpenAiClient(limitedHttp, new() { DefaultModel = new() { ModelId = "model" }, CredentialResolver = Credentials.FromStatic("key"), MaxResponseBytes = 1024 });
        Assert.Equal(StructuredErrorKind.InvalidResponse, (await limitedClient.ImportEmbeddingBatchResultsAsync("batch", manifest, cancellationToken: TestContext.Current.CancellationToken)).Error!.Kind);
    }

    [Theory]
    [InlineData(false, StructuredErrorKind.Refused)]
    [InlineData(true, StructuredErrorKind.IncompleteOutput)]
    public async Task TestingRefusalAndIncompleteEnvelopesUseRealResponseProcessing(bool incomplete, StructuredErrorKind expected)
    {
        using var handler = new Handler(_ => ResponseEnvelopes.ToHttpResponse(incomplete ? ResponseEnvelopes.Incomplete("{partial") : ResponseEnvelopes.Refusal()));
        using var http = new HttpClient(handler);
        var task = StructuredTask.Create<Answer>(new() { Instructions = "Extract" });
        Assert.Equal(expected, (await Client(http).ExecuteAsync(task, new() { Input = "input" }, TestContext.Current.CancellationToken)).Error!.Kind);
    }

    [Fact]
    public void CompletionPreservesArraysNullsAndNestedSuppliedValues()
    {
        var task = StructuredTask.Create<NestedAnswer>(new());
        var result = ResponseEnvelopes.CompleteExample(task, "{\"child\":{\"value\":\"keep\"},\"items\":[\"first\"],\"optional\":null}");
        Assert.Equal("keep", result.GetProperty("child").GetProperty("value").GetString());
        Assert.Equal("first", result.GetProperty("items")[0].GetString());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("optional").ValueKind);
        Assert.Throws<ArgumentException>(() => StructuredTask.Create<PatternAnswer>(new()).CreateExample());
    }

    public sealed class NestedAnswer
    {
        public required Answer Child { get; init; }
        public required string[] Items { get; init; }
        public string? Optional { get; init; }
    }
    public sealed class PatternAnswer { [StringConstraint(Pattern = "^a+$")] public required string Value { get; init; } }
    sealed class TrackedContent : StringContent
    {
        public TrackedContent() : base("{}") { }
        public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override void Dispose(bool disposing) { base.Dispose(disposing); if (disposing) Disposed.TrySetResult(); }
    }
    sealed class ManualClock : TimeProvider
    {
        long _ticks;
        readonly List<Timer> _timers = [];
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new Timer(this, callback, state); timer.Change(dueTime, period); _timers.Add(timer); return timer;
        }
        public void Advance(TimeSpan duration) { _ticks += duration.Ticks; foreach (var timer in _timers.ToArray()) timer.Fire(); }
        sealed class Timer(ManualClock clock, TimerCallback callback, object? state) : ITimer
        {
            long _due = Int64.MaxValue;
            public bool Change(TimeSpan dueTime, TimeSpan period) { _due = dueTime == Timeout.InfiniteTimeSpan ? Int64.MaxValue : clock._ticks + dueTime.Ticks; return true; }
            public void Fire() { if (clock._ticks < _due) return; _due = Int64.MaxValue; callback(state); }
            public void Dispose() => _due = Int64.MaxValue;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }

    public sealed class Answer { public required string Value { get; init; } }
    public sealed class OtherAnswer { public required int Count { get; init; } }
    sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) { Calls++; return Task.FromResult(send(request)); }
    }
    sealed class AsyncHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
}
