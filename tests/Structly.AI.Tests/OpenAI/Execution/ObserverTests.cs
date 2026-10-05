using static Structly.AI.Tests.ReliabilityTestSupport;

namespace Structly.AI.Tests;

public sealed class ObserverTests
{
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
        var clock = new ReliabilityClock();
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
            },
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
        var clock = new ReliabilityClock();
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
            },
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
            },
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
        var clock = new ReliabilityClock();
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
            },
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
        },
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
        var clock = new ReliabilityClock();
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
            },
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
        var clock = new ReliabilityClock();
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
            },
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

    [Fact]
    public async Task ExhaustedBudgetSkipsUsageObserverWithoutLosingUsage()
    {
        var clock = new ReliabilityClock();
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
            },
            }, TestToken);
        Assert.Equal(StructuredErrorKind.DeadlineExceeded, result.Error!.Kind);
        Assert.Equal(10, result.Metadata.Usage!.InputTokens);
        Assert.Contains(result.Warnings, w => w.Code == "UsageObserverSkipped");
    }

}
