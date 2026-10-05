using static Structly.AI.Tests.ReliabilityTestSupport;

namespace Structly.AI.Tests;

public sealed class CancellationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task HostCancellationDuringMaterializationPreservesUsageAndExecutionPrecedence(int cancellation)
    {
        var clock = new ReliabilityClock();
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

}
