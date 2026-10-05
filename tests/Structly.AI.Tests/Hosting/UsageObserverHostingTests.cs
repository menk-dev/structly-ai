using Microsoft.Extensions.DependencyInjection;
using Structly.AI.Hosting;
using Structly.AI.OpenAI;
using Structly.AI.Testing;
using System.Collections.Concurrent;
using static Structly.AI.Tests.BatchTestSupport;

namespace Structly.AI.Tests;

public sealed class UsageObserverHostingTests
{
    [Fact]
    public async Task ConcurrentCallbacksUseIndependentScopesAndDisposeAsyncDependencies()
    {
        var services = new ServiceCollection();
        var state = new RecorderState();
        services.AddSingleton(state);
        services.AddScoped<ScopeDependency>();
        var registration = services.AddStructlyOpenAi(options =>
        {
            options.DefaultModel = new() { ModelId = "embedding" };
            options.ApiKey = "offline";
            options.UsageObserver = (_, _) => throw new InvalidOperationException("Options observer must be overridden.");
        }).AddUsageObserver<Recorder>()
            .ConfigurePrimaryHttpMessageHandler(() => new Handler(_ => Response(ResponseEnvelopes.Embeddings([new float[] { 1, 2 }], "embedding", new() { InputTokens = 7 }))));
        Assert.Throws<InvalidOperationException>(() => registration.AddUsageObserver<Recorder>());
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var client = provider.GetRequiredService<OpenAiClient>();
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => client.EmbedAsync(new() { ModelSelection = new() { ModelId = "embedding" }, Inputs = ["input"] }, TestContext.Current.CancellationToken)));
        Assert.All(results, result => Assert.True(result.IsSuccess));
        Assert.All(results, result => Assert.Empty(result.Warnings));
        Assert.Equal(4, state.Observed.Count);
        Assert.Equal(4, state.Observed.Distinct().Count());
        Assert.Equal(state.Observed.Order(), state.Disposed.Order());

        var overridden = 0;
        await client.EmbedAsync(new()
        {
            ModelSelection = new() { ModelId = "embedding" },
            Inputs = ["input"],
            UsageObserver = (_, _) =>
            {
                overridden++;
                return ValueTask.CompletedTask;
            },
        }, TestContext.Current.CancellationToken);
        Assert.Equal(1, overridden);
        Assert.Equal(4, state.Observed.Count);
    }

    [Fact]
    public async Task ExistingConcreteRegistrationIsRespectedAndFailureIsBestEffort()
    {
        var services = new ServiceCollection();
        var recorder = new FailingRecorder();
        services.AddSingleton(recorder);
        services.AddStructlyOpenAi(options =>
        {
            options.DefaultModel = new() { ModelId = "embedding" };
            options.ApiKey = "offline";
        }).AddUsageObserver<FailingRecorder>()
            .ConfigurePrimaryHttpMessageHandler(() => new Handler(request => request.RequestUri!.AbsolutePath.EndsWith("batch", StringComparison.Ordinal) ? Response(Job()) :
                new(System.Net.HttpStatusCode.OK) { Content = new StringContent(request.RequestUri.AbsolutePath.Contains("errors", StringComparison.Ordinal) ? "" : Line("a", Vectors())) }));
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var client = provider.GetRequiredService<OpenAiClient>();
        var manifest = client.PrepareEmbeddingBatch([Item("a")]).Manifest;
        var imported = await client.ImportEmbeddingBatchResultsAsync("batch", manifest, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(imported.IsSuccess);
        Assert.Equal(1, recorder.Calls);
        Assert.Contains(imported.Warnings, warning => warning.Code == "UsageObserverFailed");
        var overridden = 0;
        await client.ImportEmbeddingBatchResultsAsync("batch", manifest, new()
        {
            UsageObserver = (_, _) =>
            {
                overridden++;
                return ValueTask.CompletedTask;
            },
        }, TestContext.Current.CancellationToken);
        Assert.Equal(1, overridden);
        Assert.Equal(1, recorder.Calls);
    }

    sealed class RecorderState
    {
        public ConcurrentBag<Guid> Observed { get; } = [];
        public ConcurrentBag<Guid> Disposed { get; } = [];
    }

    sealed class ScopeDependency(RecorderState state) : IAsyncDisposable
    {
        public Guid Id { get; } = Guid.NewGuid();
        public async ValueTask DisposeAsync()
        {
            await Task.Yield();
            state.Disposed.Add(Id);
        }
    }

    sealed class Recorder(ScopeDependency dependency, RecorderState state) : IStructuredUsageObserver
    {
        public async ValueTask ObserveAsync(StructuredUsageEvent usage, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            state.Observed.Add(dependency.Id);
        }
    }

    sealed class FailingRecorder : IStructuredUsageObserver
    {
        public int Calls { get; private set; }
        public ValueTask ObserveAsync(StructuredUsageEvent usage, CancellationToken cancellationToken)
        {
            Calls++;
            throw new InvalidOperationException("Recorder failed.");
        }
    }
}
