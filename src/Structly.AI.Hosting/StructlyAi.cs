using Microsoft.Extensions.DependencyInjection;
using Structly.AI.OpenAI;
using System.Collections.Frozen;
using System.Runtime.CompilerServices;

namespace Structly.AI.Hosting;

/// <summary>Executes registered AI tasks with a fresh client and dependency injection scope per call.</summary>
public sealed class StructlyAi
{
    readonly IServiceScopeFactory _scopeFactory;
    readonly FrozenDictionary<string, object> _tasks;

    internal StructlyAi(IServiceScopeFactory scopeFactory, FrozenDictionary<string, object> tasks)
    {
        _scopeFactory = scopeFactory;
        _tasks = tasks;
    }

    /// <summary>Executes a named task with text input.</summary>
    public Task<StructuredResult<T>> ExecuteTaskAsync<T>(string name, string input, CancellationToken cancellationToken = default)
        => ExecuteTaskAsync<T>(name, new StructuredRequest { Input = input }, cancellationToken);

    /// <summary>Executes a named task with per-call settings. Lookup and output-type errors throw before client resolution.</summary>
    [OverloadResolutionPriority(1)]
    public async Task<StructuredResult<T>> ExecuteTaskAsync<T>(string name, StructuredRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(request);
        if(!_tasks.TryGetValue(name, out var definition))
            throw new KeyNotFoundException("No AI task is registered with this name.");

        if(definition is not NamedAiTask<T> task)
            throw new InvalidOperationException("The registered AI task has a different output type.");

        await using var scope = _scopeFactory.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<OpenAiClient>();
        return await task.ExecuteAsync(client, request, cancellationToken).ConfigureAwait(false);
    }
}
