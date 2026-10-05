using System.Net;
using static Structly.AI.Tests.ReliabilityTestSupport;

namespace Structly.AI.Tests;

public sealed class DeadlineTests
{
    [Fact]
    public async Task TotalBudgetBoundsNoncooperativeCredentialsWithoutSending()
    {
        var clock = new ReliabilityClock();
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
        var clock = new ReliabilityClock();
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
    public async Task TotalWinsInactivityWhileActiveLinesCannotExtendTotal()
    {
        var clock = new ReliabilityClock();
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BodyDeadlineCleansUpAndLeavesCallerTransportReusable(bool streaming)
    {
        var clock = new ReliabilityClock();
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
        var clock = new ReliabilityClock();
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

}
