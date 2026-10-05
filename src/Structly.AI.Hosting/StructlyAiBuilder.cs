using System.Collections.Frozen;
using System.Runtime.CompilerServices;

namespace Structly.AI.Hosting;

/// <summary>Registers reusable, named structured AI tasks.</summary>
public sealed class StructlyAiBuilder : AiProviderBuilder
{
    readonly Dictionary<string, object> _tasks = new(StringComparer.Ordinal);
    bool _frozen;

    internal StructlyAiBuilder() { }

    /// <summary>Creates and registers a task with the supplied instructions.</summary>
    public StructlyAiBuilder AddTask<T>(string name, string instructions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instructions);
        return AddTask<T>(name, new StructuredTaskOptions { Instructions = instructions });
    }

    /// <summary>Creates and validates a task during registration.</summary>
    [OverloadResolutionPriority(1)]
    public StructlyAiBuilder AddTask<T>(string name, StructuredTaskOptions options)
        => AddTask(name, StructuredTask.Create<T>(options));

    /// <summary>Registers an existing task, retaining support for per-call output settings.</summary>
    public StructlyAiBuilder AddTask<T>(string name, StructuredTask<T> task)
    {
        ArgumentNullException.ThrowIfNull(task);
        return Add(name, new NamedAiTask<T>(task, null));
    }

    /// <summary>Registers an output contract with captured vocabularies and guidance.</summary>
    public StructlyAiBuilder AddTask<T>(string name, BoundOutput<T> output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return Add(name, new NamedAiTask<T>(null, output));
    }

    /// <summary>Registers a typed reference with instructions.</summary>
    public StructlyAiBuilder AddTask<T>(AiTaskReference<T> reference, string instructions)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return AddTask<T>(reference.Name, instructions);
    }

    /// <summary>Registers a typed reference with options.</summary>
    [OverloadResolutionPriority(1)]
    public StructlyAiBuilder AddTask<T>(AiTaskReference<T> reference, StructuredTaskOptions options)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return AddTask<T>(reference.Name, options);
    }

    /// <summary>Registers a typed reference with an existing task.</summary>
    public StructlyAiBuilder AddTask<T>(AiTaskReference<T> reference, StructuredTask<T> task)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return AddTask(reference.Name, task);
    }

    /// <summary>Registers a typed reference with bound output.</summary>
    public StructlyAiBuilder AddTask<T>(AiTaskReference<T> reference, BoundOutput<T> output)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return AddTask(reference.Name, output);
    }

    StructlyAiBuilder Add(string name, object task)
    {
        if(_frozen)
            throw new InvalidOperationException("Task registration has completed.");

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if(!_tasks.TryAdd(name, task))
            throw new ArgumentException("A task with this name is already registered.", nameof(name));

        return this;
    }

    internal FrozenDictionary<string, object> Freeze()
    {
        _frozen = true;
        FreezeProvider();
        return _tasks.ToFrozenDictionary(StringComparer.Ordinal);
    }
}
