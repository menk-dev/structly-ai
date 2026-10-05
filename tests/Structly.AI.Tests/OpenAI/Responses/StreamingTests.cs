using Structly.AI.OpenAI;
using System.Text.Json;
using static Structly.AI.Tests.ReliabilityTestSupport;

namespace Structly.AI.Tests;

public sealed class StreamingTests
{
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
            OpenAi = new() { CaptureRawResponse = true },
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
        var clock = new ReliabilityClock();
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
    public async Task CancelledStreamingRetainsKnownIdsAndCleansUp()
    {
        var clock = new ReliabilityClock();
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
    public async Task StreamingCeilingAndWrongContentTypeFailWithoutPublishingOutput()
    {
        using var handler = new Handler(_ => Task.FromResult(Response(new FragmentStream(Event("response.completed", Envelope())))));
        using var http = new HttpClient(handler);
        var client = new OpenAiClient(http, new()
        {
            DefaultModel = new() { ModelId = "configured" },
            CredentialResolver = Credentials.FromStatic("synthetic"),
            MaxResponseBytes = 32,
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
            },
        }, TestToken);
        Assert.True(result.IsSuccess);
        Assert.DoesNotContain(progress, p => p.Kind == StructuredProgressKind.ReasoningSummaryDelta);
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
            InactivityTimeout = TimeSpan.FromSeconds(seconds),
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
            InactivityTimeout = TimeSpan.MaxValue,
        });
        Assert.True((await client.ExecuteAsync(Contract(), new() { Input = "input", Stream = true }, TestToken)).IsSuccess);
        Assert.True((await client.ExecuteAsync(Contract(), new() { Input = "input", Stream = true, InactivityTimeout = TimeSpan.MaxValue }, TestToken)).IsSuccess);
    }

}
