using System.Net;
using System.Text.Json;
using static Structly.AI.Tests.EmbeddingAndImageTestSupport;

namespace Structly.AI.Tests;

public sealed class AuxiliaryExecutionTests
{
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
            switch(operation)
            {
                case "text":
                    await client.GenerateTextAsync(new() { Request = new() { Input = "prompt" } }, cancelled.Token);
                    break;
                case "prewarm":
                    await client.PrewarmAsync(new() { Request = new() { Input = "prompt" } }, cancelled.Token);
                    break;
                case "embedding":
                    await client.EmbedAsync(Embedding(), cancelled.Token);
                    break;
                default:
                    await client.GenerateImagesAsync(Image(), cancelled.Token);
                    break;
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
        var clock = new AuxiliaryClock();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var responseGate = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new Handler(_ =>
        {
            entered.SetResult();
            return responseGate.Task;
        });
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
            if(message.RequestUri!.AbsolutePath.EndsWith("embeddings", StringComparison.Ordinal))
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
            CredentialResolver = _ =>
            {
                entered.SetResult();
                return new(release.Task);
            },
            UsageObserver = (item, _) =>
            {
                observers.Add(item.Metadata.Operation!);
                return ValueTask.CompletedTask;
            },
        }, TestContext.Current.CancellationToken);
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        inputs[0] = "mutated";
        var image = await client.GenerateImagesAsync(Image() with
        {
            CredentialResolver = Credentials.FromStatic("image-key"),
            UsageObserver = (item, _) =>
            {
                observers.Add(item.Metadata.Operation!);
                return ValueTask.CompletedTask;
            },
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
            if(image)
                await client.GenerateImagesAsync(Image() with { UsageObserver = Observer }, caller.Token);
            else
                await client.EmbedAsync(Embedding() with { UsageObserver = Observer }, caller.Token);
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

}
