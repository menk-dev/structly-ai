using Structly.AI.OpenAI;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace Structly.AI.Tests;

public sealed class ReliabilityTests
{
    static CancellationToken TestToken => TestContext.Current.CancellationToken;
    static StructuredTask<Answer> Contract() => StructuredTask.Create<Answer>(new() { Instructions = "Extract" });
    static string Envelope(string status = "completed", string text = "{\"value\":42}", bool usage = true) => JsonSerializer.Serialize(new
    {
        id = "response",
        model = "resolved",
        status,
        usage = usage ? new { input_tokens = 10, output_tokens = 5 } : null,
        output = new[] { new { type = "message", role = "assistant", status = "completed", content = new[] { new { type = "output_text", text } } } }
    });
    static string Event(string type, string envelope) => $"event: {type}\r\ndata: {{\"type\":\"{type}\",\"response\":{envelope}}}\r\n\r\n";
    static HttpResponseMessage Response(Stream stream, bool sse = true)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) };
        response.Content.Headers.ContentType = new(sse ? "text/event-stream" : "application/json");
        response.Headers.Add("x-request-id", "request");
        return response;
    }
    static OpenAiClient Client(HttpClient http, ManualClock clock, Func<StructuredUsageEvent, CancellationToken, ValueTask>? observer = null) => new(http,
        new()
        {
            DefaultModel = new() { ModelId = "configured" },
            CredentialResolver = Credentials.FromStatic("synthetic"),
            TimeProvider = clock,
            TotalTimeout = TimeSpan.FromSeconds(20),
            UsageObserver = observer
        });
    static TaskCompletionSource<T> Gate<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Fact]
    public async Task TotalBudgetBoundsNoncooperativeCredentialsWithoutSending()
    {
        var clock = new ManualClock();
        var credential = Gate<string?>();
        using var handler = new Handler(_ => throw new InvalidOperationException("No HTTP expected"));
        using var http = new HttpClient(handler);
        var pending = Client(http, clock).ExecuteAsync(Contract(), new() { Input = "input", CredentialResolver = _ => new(credential.Task) }, TestToken);
        clock.Advance(TimeSpan.FromSeconds(20));
        var result = await pending.WaitAsync(TestToken);
        Assert.Equal(StructuredErrorKind.DeadlineExceeded, result.Error!.Kind);
        Assert.Equal(TimeSpan.FromSeconds(20), result.Error.TotalTimeout);
        Assert.Contains("Total timeout", result.Error.Message);
        Assert.True(result.Error.IsTransient);
        Assert.Equal(0, handler.Calls);
        credential.SetException(new InvalidOperationException("secret late fault"));
    }

    [Fact]
    public async Task CallerWinsSimultaneousDeadlineAndNoncooperativeSendDisposesLateResponse()
    {
        var clock = new ManualClock();
        var sent = Gate<HttpResponseMessage>();
        using var handler = new Handler(_ => sent.Task);
        using var http = new HttpClient(handler);
        using var caller = new CancellationTokenSource();
        var pending = Client(http, clock).ExecuteAsync(Contract(), new() { Input = "input" }, caller.Token);
        caller.Cancel();
        clock.Advance(TimeSpan.FromSeconds(20));
        var exception = await Assert.ThrowsAsync<StructuredOperationCanceledException>(() => pending);
        Assert.Equal(caller.Token, exception.CancellationToken);
        Assert.Equal("configured", exception.Metadata.RequestedModel);
        var stream = new FragmentStream(Envelope());
        sent.SetResult(Response(stream, false));
        await stream.Disposed.Task.WaitAsync(TestToken);
        Assert.Equal(1, handler.Calls);
        Assert.Null(http.DefaultRequestHeaders.Authorization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(86401)]
    public async Task InvalidBudgetsFailLocally(double seconds)
    {
        using var handler = new Handler(_ => throw new InvalidOperationException());
        using var http = new HttpClient(handler);
        var result = await Client(http, new()).ExecuteAsync(Contract(), new() { Input = "input", TotalTimeout = TimeSpan.FromSeconds(seconds) }, TestToken);
        Assert.Equal(StructuredErrorKind.InvalidRequest, result.Error!.Kind);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task NonstreamingProgressAndInactivityAreRejectedBeforeAuth()
    {
        using var handler = new Handler(_ => throw new InvalidOperationException());
        using var http = new HttpClient(handler);
        var client = Client(http, new());
        StructuredRequest[] requests = [new() { Input = "input", Progress = (_, _) => ValueTask.CompletedTask },
            new() { Input = "input", InactivityTimeout = TimeSpan.FromSeconds(1) }, new() { Input = "input", IncludeReasoningSummary = true }];
        foreach(var request in requests)
        {
            var result = await client.ExecuteAsync(Contract(), request with { CredentialResolver = _ => throw new InvalidOperationException() }, TestToken);
            Assert.Equal(StructuredErrorKind.InvalidRequest, result.Error!.Kind);
        }
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task SplitMultilineSseKeepsOrderAndOnlyExposesOptedInSummaries()
    {
        var events = new List<StructuredProgress>();
        var body = "\uFEFF: keepalive\r\n\r\n" + Event("response.created", "{\"id\":\"response\",\"model\":\"resolved\"}") +
            "event: response.output_text.delta\ndata: {\"type\":\"response.output_text.delta\",\ndata: \"delta\":\"é\",\"output_index\":0}\n\n" +
            "data: {\"type\":\"response.reasoning_text.delta\",\"delta\":\"RAW_SECRET\"}\n\n" +
            "data: {\"type\":\"response.reasoning_summary_text.delta\",\"delta\":\"summary\",\"output_index\":1,\"summary_index\":2}\n\n" +
            "data: {\"type\":\"future.event\"}\n\n" + Event("response.completed", Envelope());
        using var stream = new FragmentStream(body, 1);
        using var handler = new Handler(async message =>
        {
            using var json = JsonDocument.Parse(await message.Content!.ReadAsStringAsync(TestToken));
            Assert.True(json.RootElement.GetProperty("stream").GetBoolean());
            Assert.Equal("auto", json.RootElement.GetProperty("reasoning").GetProperty("summary").GetString());
            return Response(stream);
        });
        using var http = new HttpClient(handler);
        var result = await Client(http, new()).ExecuteAsync(Contract(), new()
        {
            Input = "input",
            Stream = true,
            IncludeReasoningSummary = true,
            Progress = (item, _) =>
            {
                events.Add(item);
                return ValueTask.CompletedTask;
            },
            OpenAi = new() { CaptureRawResponse = true }
        }, TestToken);
        Assert.Equal(42, result.EnsureSuccess().Value);
        Assert.Equal([StructuredProgressKind.Started, StructuredProgressKind.OutputTextDelta, StructuredProgressKind.ReasoningSummaryDelta, StructuredProgressKind.Completed], events.Select(e => e.Kind));
        Assert.Equal("é", events[1].TextDelta);
        Assert.Equal(2, events[2].SummaryIndex);
        Assert.Equal(1, events[2].OutputIndex);
        Assert.Equal("response", events[2].ResponseId);
        Assert.DoesNotContain(events, e => e.TextDelta == "RAW_SECRET");
        Assert.Equal("completed", result.Metadata.RawResponse!.Value.GetProperty("status").GetString());
        Assert.True(stream.Disposed.Task.IsCompleted);
    }

    [Theory]
    [InlineData("completed", "{\"value\":42}", null)]
    [InlineData("completed", "{\"value\":\"bad\"}", StructuredErrorKind.InvalidOutput)]
    [InlineData("incomplete", "{\"value\":42}", StructuredErrorKind.IncompleteOutput)]
    [InlineData("failed", "{\"value\":42}", StructuredErrorKind.ProviderRejected)]
    public async Task StreamingUsesTerminalOutputAndRetainsBilledFailures(string status, string text, StructuredErrorKind? error)
    {
        var notifications = new List<StructuredUsageEvent>();
        using var stream = new FragmentStream(Event("response." + status, Envelope(status, text)));
        using var handler = new Handler(_ => Task.FromResult(Response(stream)));
        using var http = new HttpClient(handler);
        var result = await Client(http, new(), (item, _) =>
        {
            notifications.Add(item);
            return ValueTask.CompletedTask;
        })
            .ExecuteAsync(Contract(), new() { Input = "input", Stream = true }, TestToken);
        Assert.Equal(error, result.Error?.Kind);
        Assert.Equal(10, result.Metadata.Usage!.InputTokens);
        var notification = Assert.Single(notifications);
        Assert.Equal(error, notification.FailureKind);
        Assert.Equal(result.IsSuccess, notification.Succeeded);
        Assert.Equal(result.Metadata.ExecutionId, notification.Metadata.ExecutionId);
        Assert.True(stream.Disposed.Task.IsCompleted);
    }

    [Theory]
    [InlineData("data: broken\n\n")]
    [InlineData("data: {\"type\":\"response.output_text.delta\",\"delta\":\"x\"}\n\n")]
    [InlineData("data: {\"type\":\"response.output_text.delta\",\"delta\":5,\"output_index\":0}\n\n")]
    [InlineData("event: response.completed\ndata: {\"type\":\"other\"}\n\n")]
    [InlineData("data: {\"type\":\"response.completed\",\"response\":{\"status\":\"incomplete\"}}\n\n")]
    [InlineData("data: {\"type\":\"response.output_text.delta\",\"delta\":\"{}\",\"output_index\":0}\n\n")]
    [InlineData("data: {\"type\":\"error\",\"message\":\"secret\"}\n\n")]
    public async Task MalformedOrUnterminatedStreamsFailSafelyAndDispose(string body)
    {
        using var stream = new FragmentStream(body);
        using var handler = new Handler(_ => Task.FromResult(Response(stream)));
        using var http = new HttpClient(handler);
        var result = await Client(http, new()).ExecuteAsync(Contract(), new() { Input = "input", Stream = true }, TestToken);
        Assert.Equal(StructuredErrorKind.InvalidResponse, result.Error!.Kind);
        Assert.DoesNotContain("secret", result.Error.Message);
        Assert.True(stream.Disposed.Task.IsCompleted);
    }

    [Fact]
    public async Task CompleteKeepaliveLinesResetInactivityButPartialBytesDoNot()
    {
        var clock = new ManualClock();
        using var stream = new ControlledStream();
        using var handler = new Handler(_ => Task.FromResult(Response(stream)));
        using var http = new HttpClient(handler);
        var pending = Client(http, clock).ExecuteAsync(Contract(), new() { Input = "input", Stream = true, InactivityTimeout = TimeSpan.FromSeconds(3) }, TestToken);
        await stream.NextRead();
        for(var index = 0; index < 3; index++)
        {
            clock.Advance(TimeSpan.FromSeconds(2));
            stream.Push(": keepalive\n");
            await stream.NextRead();
            Assert.False(pending.IsCompleted);
        }
        clock.Advance(TimeSpan.FromSeconds(2));
        stream.Push(": unfinished");
        await stream.NextRead();
        clock.Advance(TimeSpan.FromSeconds(1));
        var result = await pending.WaitAsync(TestToken);
        Assert.Equal(StructuredErrorKind.InactivityExceeded, result.Error!.Kind);
        Assert.Equal(TimeSpan.FromSeconds(3), result.Error.InactivityTimeout);
        Assert.Contains("Inactivity timeout", result.Error.Message);
        Assert.True(stream.IsDisposed);
        Assert.Equal("request", result.Metadata.ProviderRequestId);
    }

    [Fact]
    public async Task TotalWinsInactivityWhileActiveLinesCannotExtendTotal()
    {
        var clock = new ManualClock();
        using var stream = new ControlledStream();
        using var handler = new Handler(_ => Task.FromResult(Response(stream)));
        using var http = new HttpClient(handler);
        var pending = Client(http, clock).ExecuteAsync(Contract(), new() { Input = "input", Stream = true, TotalTimeout = TimeSpan.FromSeconds(6), InactivityTimeout = TimeSpan.FromSeconds(3) }, TestToken);
        await stream.NextRead();
        clock.Advance(TimeSpan.FromSeconds(2));
        stream.Push(": ping\n");
        await stream.NextRead();
        clock.Advance(TimeSpan.FromSeconds(1));
        stream.Push(": ping\n");
        await stream.NextRead();
        clock.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal(StructuredErrorKind.DeadlineExceeded, (await pending.WaitAsync(TestToken)).Error!.Kind);
        Assert.True(stream.IsDisposed);
    }

    [Fact]
    public async Task CancelledStreamingRetainsKnownIdsAndCleansUp()
    {
        var clock = new ManualClock();
        using var stream = new ControlledStream();
        stream.Push(Event("response.created", "{\"id\":\"known\",\"model\":\"resolved\"}"));
        using var handler = new Handler(_ => Task.FromResult(Response(stream)));
        using var http = new HttpClient(handler);
        using var caller = new CancellationTokenSource();
        var pending = Client(http, clock).ExecuteAsync(Contract(), new() { Input = "input", Stream = true }, caller.Token);
        await stream.NextRead();
        await stream.NextRead();
        caller.Cancel();
        var exception = await Assert.ThrowsAsync<StructuredOperationCanceledException>(() => pending);
        Assert.Equal("known", exception.Metadata.ResponseId);
        Assert.Equal("request", exception.Metadata.ProviderRequestId);
        Assert.True(stream.IsDisposed);
    }

    [Fact]
    public async Task UsageObserverFailureNeverMasksBilledOutputFailure()
    {
        using var stream = new FragmentStream(Envelope(text: "{}"));
        using var handler = new Handler(_ => Task.FromResult(Response(stream, false)));
        using var http = new HttpClient(handler);
        var result = await Client(http, new(), (_, _) => throw new InvalidOperationException("secret"))
            .ExecuteAsync(Contract(), new() { Input = "input" }, TestToken);
        Assert.Equal(StructuredErrorKind.InvalidOutput, result.Error!.Kind);
        Assert.Equal("UsageObserverFailed", Assert.Single(result.Warnings).Code);
        Assert.DoesNotContain("secret", result.Warnings[0].Message);
        Assert.NotNull(result.Metadata.Usage);
    }

    [Fact]
    public async Task ObserverOwnTimeoutPreservesSuccessAndObservesLateFault()
    {
        var clock = new ManualClock();
        var entered = Gate<bool>();
        var callback = Gate<bool>();
        using var handler = new Handler(_ => Task.FromResult(Response(new FragmentStream(Envelope()), false)));
        using var http = new HttpClient(handler);
        var pending = Client(http, clock).ExecuteAsync(Contract(), new()
        {
            Input = "input",
            UsageObserver = (_, token) =>
            {
                Assert.False(token.IsCancellationRequested);
                entered.SetResult(true);
                return new(callback.Task);
            }
        }, TestToken);
        await entered.Task.WaitAsync(TestToken);
        clock.Advance(TimeSpan.FromSeconds(5));
        var result = await pending.WaitAsync(TestToken);
        Assert.True(result.IsSuccess);
        Assert.Equal("UsageObserverTimedOut", Assert.Single(result.Warnings).Code);
        callback.SetException(new InvalidOperationException("late secret"));
    }

    [Fact]
    public async Task TotalBudgetIncludesObserverAndSkipsWhenAlreadyExhausted()
    {
        var clock = new ManualClock();
        var entered = Gate<bool>();
        var callback = Gate<bool>();
        using var handler = new Handler(_ => Task.FromResult(Response(new FragmentStream(Envelope()), false)));
        using var http = new HttpClient(handler);
        var pending = Client(http, clock).ExecuteAsync(Contract(), new()
        {
            Input = "input",
            TotalTimeout = TimeSpan.FromSeconds(2),
            UsageObserver = (_, _) =>
            {
                entered.SetResult(true);
                return new(callback.Task);
            }
        }, TestToken);
        await entered.Task.WaitAsync(TestToken);
        clock.Advance(TimeSpan.FromSeconds(2));
        var result = await pending.WaitAsync(TestToken);
        Assert.Equal(StructuredErrorKind.DeadlineExceeded, result.Error!.Kind);
        Assert.Equal(10, result.Metadata.Usage!.InputTokens);
        Assert.Equal("UsageObserverTimedOut", Assert.Single(result.Warnings).Code);
        callback.SetResult(true);
    }

    [Fact]
    public async Task CallerCancellationDuringProgressStillDeliversUsageWithIndependentToken()
    {
        using var caller = new CancellationTokenSource();
        var observed = new List<StructuredUsageEvent>();
        using var handler = new Handler(_ => Task.FromResult(Response(new FragmentStream(Event("response.completed", Envelope())))));
        using var http = new HttpClient(handler);
        var client = Client(http, new(), (item, token) =>
        {
            Assert.False(token.IsCancellationRequested);
            observed.Add(item);
            return ValueTask.CompletedTask;
        });
        var exception = await Assert.ThrowsAsync<StructuredOperationCanceledException>(() => client.ExecuteAsync(Contract(), new()
        {
            Input = "input",
            Stream = true,
            Progress = (item, _) =>
            {
                if(item.Kind == StructuredProgressKind.Completed)
                    caller.Cancel();
                return ValueTask.CompletedTask;
            }
        }, caller.Token));
        Assert.Equal(10, exception.Metadata.Usage!.InputTokens);
        Assert.True(Assert.Single(observed).CallerCancelled);
        Assert.False(observed[0].Succeeded);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProgressFailureDisablesFurtherProgressButNotUsage(bool timeout)
    {
        var clock = new ManualClock();
        var entered = Gate<bool>();
        var callback = Gate<bool>();
        var progressCalls = 0;
        var usageCalls = 0;
        using var handler = new Handler(_ => Task.FromResult(Response(new FragmentStream(Event("response.completed", Envelope())))));
        using var http = new HttpClient(handler);
        var pending = Client(http, clock, (_, _) =>
        {
            usageCalls++;
            return ValueTask.CompletedTask;
        }).ExecuteAsync(Contract(), new()
        {
            Input = "input",
            Stream = true,
            Progress = (_, _) =>
            {
                progressCalls++;
                entered.TrySetResult(true);
                if(!timeout)
                    throw new InvalidOperationException("secret");
                return new(callback.Task);
            }
        }, TestToken);
        await entered.Task.WaitAsync(TestToken);
        if(timeout)
            clock.Advance(TimeSpan.FromSeconds(1));
        var result = await pending.WaitAsync(TestToken);
        Assert.True(result.IsSuccess);
        Assert.Equal(1, progressCalls);
        Assert.Equal(1, usageCalls);
        Assert.Equal(timeout ? "ProgressObserverTimedOut" : "ProgressObserverFailed", Assert.Single(result.Warnings).Code);
        callback.TrySetResult(true);
    }

    [Fact]
    public async Task MissingUsageDoesNotNotifyAndRequestObserverOverridesClient()
    {
        var calls = 0;
        using var handler = new Handler(_ => Task.FromResult(Response(new FragmentStream(Envelope(usage: false)), false)));
        using var http = new HttpClient(handler);
        var client = Client(http, new(), (_, _) =>
        {
            calls++;
            return ValueTask.CompletedTask;
        });
        Assert.True((await client.ExecuteAsync(Contract(), new() { Input = "input" }, TestToken)).IsSuccess);
        Assert.Equal(0, calls);
        using var billedHandler = new Handler(_ => Task.FromResult(Response(new FragmentStream(Envelope()), false)));
        using var billedHttp = new HttpClient(billedHandler);
        var local = 0;
        var result = await Client(billedHttp, new(), (_, _) => throw new InvalidOperationException()).ExecuteAsync(Contract(), new()
        {
            Input = "input",
            UsageObserver = (_, _) =>
        {
            local++;
            return ValueTask.CompletedTask;
        }
        }, TestToken);
        Assert.True(result.IsSuccess);
        Assert.Equal(1, local);
        Assert.Empty(result.Warnings);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task SlowDeltaObserversDoNotConsumeInactivityBudget(bool timeout, bool totalExpires)
    {
        var clock = new ManualClock();
        var entered = Gate<bool>();
        var callback = Gate<bool>();
        StructuredUsageEvent? observed = null;
        const string delta = "data: {\"type\":\"response.output_text.delta\",\"delta\":\"x\",\"output_index\":0}\n\n";
        using var stream = new FragmentStream(delta + Event("response.completed", Envelope()));
        using var handler = new Handler(_ => Task.FromResult(Response(stream)));
        using var http = new HttpClient(handler);
        var pending = Client(http, clock, (item, _) =>
        {
            observed = item;
            return ValueTask.CompletedTask;
        }).ExecuteAsync(Contract(), new()
        {
            Input = "input",
            Stream = true,
            TotalTimeout = TimeSpan.FromSeconds(totalExpires ? 1 : 20),
            InactivityTimeout = TimeSpan.FromMilliseconds(100),
            Progress = (item, _) =>
            {
                if(item.Kind != StructuredProgressKind.OutputTextDelta)
                    return ValueTask.CompletedTask;
                entered.SetResult(true);
                return new(callback.Task);
            }
        }, TestToken);
        await entered.Task.WaitAsync(TestToken);
        clock.Advance(TimeSpan.FromMilliseconds(timeout ? 1000 : 500));
        if(!timeout)
            callback.SetResult(true);
        var result = await pending.WaitAsync(TestToken);
        if(totalExpires)
            Assert.Equal(StructuredErrorKind.DeadlineExceeded, result.Error!.Kind);
        else
        {
            Assert.True(result.IsSuccess);
            Assert.Equal(10, result.Metadata.Usage!.InputTokens);
            Assert.True(observed!.Succeeded);
            Assert.Equal(10, observed.Metadata.Usage!.InputTokens);
            if(timeout)
                Assert.Equal("ProgressObserverTimedOut", Assert.Single(result.Warnings).Code);
            else
                Assert.Empty(result.Warnings);
        }
        Assert.True(stream.Disposed.Task.IsCompleted);
        callback.TrySetResult(true);
    }

    [Fact]
    public async Task InactivityResumesAfterSlowDeltaObserver()
    {
        var clock = new ManualClock();
        var entered = Gate<bool>();
        var callback = Gate<bool>();
        using var stream = new ControlledStream();
        using var handler = new Handler(_ => Task.FromResult(Response(stream)));
        using var http = new HttpClient(handler);
        var pending = Client(http, clock).ExecuteAsync(Contract(), new()
        {
            Input = "input",
            Stream = true,
            InactivityTimeout = TimeSpan.FromMilliseconds(100),
            Progress = (item, _) =>
            {
                if(item.Kind != StructuredProgressKind.OutputTextDelta)
                    return ValueTask.CompletedTask;
                entered.SetResult(true);
                return new(callback.Task);
            }
        }, TestToken);
        await stream.NextRead();
        stream.Push("data: {\"type\":\"response.output_text.delta\",\"delta\":\"x\",\"output_index\":0}\n\n");
        await entered.Task.WaitAsync(TestToken);
        clock.Advance(TimeSpan.FromMilliseconds(500));
        callback.SetResult(true);
        await stream.NextRead();
        clock.Advance(TimeSpan.FromMilliseconds(100));
        Assert.Equal(StructuredErrorKind.InactivityExceeded, (await pending.WaitAsync(TestToken)).Error!.Kind);
        Assert.True(stream.IsDisposed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task HostCancellationDuringMaterializationPreservesUsageAndExecutionPrecedence(int cancellation)
    {
        var clock = new ManualClock();
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestToken);
        StructuredUsageEvent? observed = null;
        using var handler = new Handler(_ => Task.FromResult(Response(new FragmentStream(Envelope()), false)));
        using var http = new HttpClient(handler);
        var client = Client(http, clock, (item, _) =>
        {
            observed = item;
            return ValueTask.CompletedTask;
        });
        _materializationCancellation.Value = () =>
        {
            if(cancellation == 1)
                caller.Cancel();
            if(cancellation == 2)
                clock.Advance(TimeSpan.FromSeconds(20));
            throw new OperationCanceledException("sensitive");
        };
        try
        {
            var pending = client.ExecuteAsync(StructuredTask.Create<CancellingConstructor>(new() { Instructions = "Extract" }), new() { Input = "input" }, caller.Token);
            if(cancellation == 1)
            {
                var exception = await Assert.ThrowsAsync<StructuredOperationCanceledException>(async () => await pending);
                Assert.Equal(caller.Token, exception.CancellationToken);
                Assert.Equal(10, exception.Metadata.Usage!.InputTokens);
                Assert.True(observed!.CallerCancelled);
            }
            else
            {
                var result = await pending;
                Assert.Equal(cancellation == 2 ? StructuredErrorKind.DeadlineExceeded : StructuredErrorKind.InvalidOutput, result.Error!.Kind);
                Assert.Equal(cancellation == 2, result.Error.IsTransient);
                Assert.Equal(10, result.Metadata.Usage!.InputTokens);
                Assert.DoesNotContain("sensitive", result.Error.Message);
                if(cancellation == 0)
                    Assert.Equal(StructuredErrorKind.InvalidOutput, observed!.FailureKind);
            }
        }
        finally
        {
            _materializationCancellation.Value = null;
        }
    }

    static readonly AsyncLocal<Action?> _materializationCancellation = new();
    public sealed class CancellingConstructor
    {
        public int Value { get; }
        public CancellingConstructor(int value) => _materializationCancellation.Value!();
    }

    [Fact]
    public async Task RetryDateUsesConfiguredClockAndStillMakesOneAttempt()
    {
        var clock = new ManualClock();
        using var handler = new Handler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent("{}") };
            response.Headers.RetryAfter = new RetryConditionHeaderValue(clock.GetUtcNow().AddSeconds(8));
            return Task.FromResult(response);
        });
        using var http = new HttpClient(handler);
        var result = await Client(http, clock).ExecuteAsync(Contract(), new() { Input = "input" }, TestToken);
        Assert.Equal(TimeSpan.FromSeconds(8), result.Error!.RetryAfter);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task ConcurrentTaskReuseIsolatesVocabularyCredentialsOptionsAndObservers()
    {
        var entered = Channel.CreateUnbounded<bool>();
        var release = Gate<bool>();
        var snapshots = new System.Collections.Concurrent.ConcurrentDictionary<string, JsonElement>();
        using var handler = new Handler(async message =>
        {
            using var document = JsonDocument.Parse(await message.Content!.ReadAsStringAsync(TestToken));
            var root = document.RootElement;
            var input = root.GetProperty("input").GetString()!;
            Assert.Equal(input + "-key", message.Headers.Authorization!.Parameter);
            snapshots[input] = root.Clone();
            entered.Writer.TryWrite(true);
            await release.Task.WaitAsync(TestToken);
            return Response(new FragmentStream(Envelope(text: JsonSerializer.Serialize(new { value = input }))), false);
        });
        using var http = new HttpClient(handler);
        var client = Client(http, new(), (_, _) => throw new InvalidOperationException("Default observer must be overridden"));
        var task = StructuredTask.Create<Choice>(new() { Instructions = "Choose" });
        var wordsA = new List<string> { "apple" };
        var wordsB = new List<string> { "pear" };
        StructuredUsageEvent? eventA = null;
        StructuredUsageEvent? eventB = null;
        var first = client.ExecuteAsync(task, new()
        {
            Input = "apple",
            CredentialResolver = Credentials.FromStatic("apple-key"),
            Vocabularies = new Dictionary<string, IReadOnlyList<string>> { ["choices"] = wordsA },
            MaxOutputTokens = 50,
            ModelSelection = new() { ModelId = "model-a" },
            OpenAi = new() { CaptureOutputText = true, Metadata = new Dictionary<string, string> { ["job"] = "a" } },
            UsageObserver = (item, _) =>
            {
                eventA = item;
                throw new InvalidOperationException("secret");
            }
        }, TestToken);
        var second = client.ExecuteAsync(task, new()
        {
            Input = "pear",
            CredentialResolver = Credentials.FromStatic("pear-key"),
            Vocabularies = new Dictionary<string, IReadOnlyList<string>> { ["choices"] = wordsB },
            MaxOutputTokens = 100,
            ModelSelection = new() { ModelId = "model-b" },
            UsageObserver = (item, _) =>
            {
                eventB = item;
                return ValueTask.CompletedTask;
            }
        }, TestToken);
        await entered.Reader.ReadAsync(TestToken);
        await entered.Reader.ReadAsync(TestToken);
        wordsA[0] = "mutated";
        wordsB[0] = "mutated";
        release.SetResult(true);
        var results = await Task.WhenAll(first, second);
        Assert.Equal("apple", results[0].EnsureSuccess().Value);
        Assert.Equal("pear", results[1].EnsureSuccess().Value);
        Assert.Equal("UsageObserverFailed", Assert.Single(results[0].Warnings).Code);
        Assert.Empty(results[1].Warnings);
        Assert.NotNull(results[0].Metadata.OutputText);
        Assert.Null(results[1].Metadata.OutputText);
        Assert.NotEqual(eventA!.Metadata.ExecutionId, eventB!.Metadata.ExecutionId);
        foreach(var input in new[] { "apple", "pear" })
        {
            var wire = snapshots[input];
            Assert.Equal(input, wire.GetProperty("text").GetProperty("format").GetProperty("schema")
                .GetProperty("properties").GetProperty("value").GetProperty("enum")[0].GetString());
            Assert.Equal(input == "apple" ? "model-a" : "model-b", wire.GetProperty("model").GetString());
            Assert.Equal(input == "apple" ? 50 : 100, wire.GetProperty("max_output_tokens").GetInt32());
        }
        Assert.False(snapshots["pear"].TryGetProperty("metadata", out _));
        Assert.Null(http.DefaultRequestHeaders.Authorization);
    }

    [Fact]
    public async Task ConcurrentRequestsHaveIndependentDeadlineSources()
    {
        var clock = new ManualClock();
        var responses = new Dictionary<string, TaskCompletionSource<HttpResponseMessage>> { ["short"] = Gate<HttpResponseMessage>(), ["long"] = Gate<HttpResponseMessage>() };
        using var handler = new Handler(async message =>
        {
            using var json = JsonDocument.Parse(await message.Content!.ReadAsStringAsync(TestToken));
            return await responses[json.RootElement.GetProperty("input").GetString()!].Task.WaitAsync(TestToken);
        });
        using var http = new HttpClient(handler);
        var client = Client(http, clock);
        var task = Contract();
        var shortCall = client.ExecuteAsync(task, new() { Input = "short", TotalTimeout = TimeSpan.FromSeconds(2) }, TestToken);
        var longCall = client.ExecuteAsync(task, new() { Input = "long", TotalTimeout = TimeSpan.FromSeconds(10) }, TestToken);
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(StructuredErrorKind.DeadlineExceeded, (await shortCall.WaitAsync(TestToken)).Error!.Kind);
        Assert.False(longCall.IsCompleted);
        responses["long"].SetResult(Response(new FragmentStream(Envelope()), false));
        Assert.True((await longCall.WaitAsync(TestToken)).IsSuccess);
        var late = new FragmentStream(Envelope());
        responses["short"].SetResult(Response(late, false));
        await late.Disposed.Task.WaitAsync(TestToken);
    }

    [Fact]
    public async Task ExhaustedBudgetSkipsUsageObserverWithoutLosingUsage()
    {
        var clock = new ManualClock();
        using var handler = new Handler(_ => Task.FromResult(Response(new FragmentStream(Event("response.completed", Envelope())))));
        using var http = new HttpClient(handler);
        var result = await Client(http, clock, (_, _) => throw new InvalidOperationException("Must be skipped"))
            .ExecuteAsync(Contract(), new()
            {
                Input = "input",
                Stream = true,
                Progress = (item, _) =>
            {
                if(item.Kind == StructuredProgressKind.Completed)
                    clock.Advance(TimeSpan.FromSeconds(20));
                return ValueTask.CompletedTask;
            }
            }, TestToken);
        Assert.Equal(StructuredErrorKind.DeadlineExceeded, result.Error!.Kind);
        Assert.Equal(10, result.Metadata.Usage!.InputTokens);
        Assert.Contains(result.Warnings, w => w.Code == "UsageObserverSkipped");
    }

    [Fact]
    public async Task StreamingCeilingAndWrongContentTypeFailWithoutPublishingOutput()
    {
        using var handler = new Handler(_ => Task.FromResult(Response(new FragmentStream(Event("response.completed", Envelope())))));
        using var http = new HttpClient(handler);
        var client = new OpenAiClient(http, new()
        {
            DefaultModel = new() { ModelId = "configured" },
            CredentialResolver = Credentials.FromStatic("synthetic"),
            MaxResponseBytes = 32
        });
        Assert.Equal(StructuredErrorKind.InvalidResponse, (await client.ExecuteAsync(Contract(), new() { Input = "input", Stream = true }, TestToken)).Error!.Kind);
        using var wrong = new Handler(_ => Task.FromResult(Response(new FragmentStream(Envelope()), false)));
        using var wrongHttp = new HttpClient(wrong);
        Assert.Equal(StructuredErrorKind.InvalidResponse, (await Client(wrongHttp, new()).ExecuteAsync(Contract(), new() { Input = "input", Stream = true }, TestToken)).Error!.Kind);
    }

    [Fact]
    public async Task UnrequestedSummaryIsNeverPublished()
    {
        var progress = new List<StructuredProgress>();
        var body = "data: {\"type\":\"response.reasoning_summary_text.delta\",\"delta\":\"summary\",\"output_index\":0,\"summary_index\":0}\n\n" + Event("response.completed", Envelope());
        using var handler = new Handler(_ => Task.FromResult(Response(new FragmentStream(body))));
        using var http = new HttpClient(handler);
        var result = await Client(http, new()).ExecuteAsync(Contract(), new()
        {
            Input = "input",
            Stream = true,
            Progress = (item, _) =>
            {
                progress.Add(item);
                return ValueTask.CompletedTask;
            }
        }, TestToken);
        Assert.True(result.IsSuccess);
        Assert.DoesNotContain(progress, p => p.Kind == StructuredProgressKind.ReasoningSummaryDelta);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BodyDeadlineCleansUpAndLeavesCallerTransportReusable(bool streaming)
    {
        var clock = new ManualClock();
        using var stalled = new ControlledStream();
        var calls = 0;
        using var handler = new Handler(_ => Task.FromResult(++calls == 1 ? Response(stalled, streaming)
            : Response(new FragmentStream(streaming ? Event("response.completed", Envelope()) : Envelope()), streaming)));
        using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        var client = Client(http, clock);
        var request = new StructuredRequest { Input = "input", Stream = streaming, TotalTimeout = TimeSpan.FromSeconds(2) };
        var pending = client.ExecuteAsync(Contract(), request, TestToken);
        await stalled.NextRead();
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(StructuredErrorKind.DeadlineExceeded, (await pending.WaitAsync(TestToken)).Error!.Kind);
        Assert.True(stalled.IsDisposed);
        Assert.True((await client.ExecuteAsync(Contract(), request, TestToken)).IsSuccess);
        Assert.Equal(Timeout.InfiniteTimeSpan, http.Timeout);
    }

    [Fact]
    public async Task TotalBudgetBoundsContentStreamAcquisitionAndDisposesLateStream()
    {
        var clock = new ManualClock();
        var acquired = Gate<Stream>();
        var entered = Gate<bool>();
        using var handler = new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new DeferredContent(acquired.Task, entered) }));
        using var http = new HttpClient(handler);
        var pending = Client(http, clock).ExecuteAsync(Contract(), new() { Input = "input" }, TestToken);
        await entered.Task.WaitAsync(TestToken);
        clock.Advance(TimeSpan.FromSeconds(20));
        Assert.Equal(StructuredErrorKind.DeadlineExceeded, (await pending.WaitAsync(TestToken)).Error!.Kind);
        var late = new FragmentStream(Envelope());
        acquired.SetResult(late);
        await late.Disposed.Task.WaitAsync(TestToken);
    }

    [Fact]
    public async Task StreamingRefusalRemainsObservableWithoutPublishingSensitiveRefusal()
    {
        StructuredUsageEvent? observed = null;
        var envelope = Envelope().Replace("\"type\":\"output_text\",\"text\":", "\"type\":\"refusal\",\"refusal\":", StringComparison.Ordinal);
        using var handler = new Handler(_ => Task.FromResult(Response(new FragmentStream(Event("response.completed", envelope)))));
        using var http = new HttpClient(handler);
        var result = await Client(http, new(), (item, _) =>
        {
            observed = item;
            return ValueTask.CompletedTask;
        })
            .ExecuteAsync(Contract(), new() { Input = "input", Stream = true }, TestToken);
        Assert.Equal(StructuredErrorKind.Refused, result.Error!.Kind);
        Assert.Equal(StructuredErrorKind.Refused, observed!.FailureKind);
        Assert.Equal(10, observed.Metadata.Usage!.InputTokens);
        Assert.Null(result.Metadata.OutputText);
        Assert.Null(result.Metadata.RawResponse);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task NonpositiveInactivityIsRejected(double seconds)
    {
        using var handler = new Handler(_ => throw new InvalidOperationException());
        using var http = new HttpClient(handler);
        var result = await Client(http, new()).ExecuteAsync(Contract(), new()
        {
            Input = "input",
            Stream = true,
            InactivityTimeout = TimeSpan.FromSeconds(seconds)
        }, TestToken);
        Assert.Equal(StructuredErrorKind.InvalidRequest, result.Error!.Kind);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task VeryLongInactivityWindowsRemainValidWithinTotalBudget()
    {
        using var handler = new Handler(_ => Task.FromResult(Response(new FragmentStream(Event("response.completed", Envelope())))));
        using var http = new HttpClient(handler);
        var client = new OpenAiClient(http, new()
        {
            DefaultModel = new() { ModelId = "configured" },
            CredentialResolver = Credentials.FromStatic("synthetic"),
            InactivityTimeout = TimeSpan.MaxValue
        });
        Assert.True((await client.ExecuteAsync(Contract(), new() { Input = "input", Stream = true }, TestToken)).IsSuccess);
        Assert.True((await client.ExecuteAsync(Contract(), new() { Input = "input", Stream = true, InactivityTimeout = TimeSpan.MaxValue }, TestToken)).IsSuccess);
    }

    sealed class DeferredContent(Task<Stream> stream, TaskCompletionSource<bool> entered) : HttpContent
    {
        protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken)
        {
            entered.TrySetResult(true);
            return stream;
        }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => throw new NotSupportedException();
        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    public sealed record Choice { [DynamicVocabulary("choices")] public required string Value { get; init; } }
    public sealed record Answer { public int Value { get; init; } }
    sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int Calls { get; set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return send(request);
        }
    }
    sealed class FragmentStream(string body, int fragment = 4096) : MemoryStream(Encoding.UTF8.GetBytes(body))
    {
        public TaskCompletionSource<bool> Disposed { get; } = Gate<bool>();
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(buffer.Length, fragment)], cancellationToken);
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if(disposing)
                Disposed.TrySetResult(true);
        }
    }
    sealed class ControlledStream : Stream
    {
        readonly Channel<byte[]> _chunks = Channel.CreateUnbounded<byte[]>();
        readonly Channel<bool> _reads = Channel.CreateUnbounded<bool>();
        public bool IsDisposed { get; set; }
        public void Push(string text) => _chunks.Writer.TryWrite(Encoding.UTF8.GetBytes(text));
        public async Task NextRead() => await _reads.Reader.ReadAsync(TestToken);
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            _reads.Writer.TryWrite(true);
            // Deliberately ignore cancellation to prove that the library abandons the wait.
            var chunk = await _chunks.Reader.ReadAsync();
            chunk.CopyTo(buffer);
            return chunk.Length;
        }
        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            _chunks.Writer.TryComplete();
            base.Dispose(disposing);
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    // Tests advance monotonic time explicitly; no sleeps or provider credentials.
    sealed class ManualClock : TimeProvider
    {
        readonly List<ManualTimer> _timers = [];
        readonly object _sync = new();
        long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp()
        {
            lock(_sync)
                return _ticks;
        }
        public override DateTimeOffset GetUtcNow() => new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddTicks(GetTimestamp());
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state);
            lock(_sync)
            {
                _timers.Add(timer);
                timer.Change(dueTime, period);
            }
            return timer;
        }
        public void Advance(TimeSpan duration)
        {
            List<ManualTimer> due;
            lock(_sync)
            {
                _ticks += duration.Ticks;
                due = _timers.Where(t => t.Due <= _ticks).ToList();
                foreach(var timer in due)
                    timer.Due = Int64.MaxValue;
            }
            foreach(var timer in due)
                timer.Fire();
        }
        sealed class ManualTimer(ManualClock clock, TimerCallback callback, object? state) : ITimer
        {
            public long Due { get; set; } = Int64.MaxValue;
            bool _disposed;
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                lock(clock._sync)
                {
                    if(_disposed)
                        return false;
                    Due = dueTime == Timeout.InfiniteTimeSpan ? Int64.MaxValue : clock._ticks + dueTime.Ticks;
                    return true;
                }
            }
            public void Fire()
            {
                lock(clock._sync)
                {
                    if(!_disposed)
                        callback(state);
                }
            }
            public void Dispose()
            {
                lock(clock._sync)
                {
                    _disposed = true;
                    Due = Int64.MaxValue;
                }
            }
            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
