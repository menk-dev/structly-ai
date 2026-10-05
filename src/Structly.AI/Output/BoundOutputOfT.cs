using System.Text.Json;

namespace Structly.AI;

/// <summary>An immutable resolved output contract and optional generated guidance.</summary>
public sealed class BoundOutput<T>
{
    readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _vocabularies;
    readonly JsonElement _schema;
    /// <summary>Gets the task defining this output contract.</summary>
    public StructuredTask<T> Task { get; }
    /// <summary>Gets the generated output guidance.</summary>
    public string? Guidance { get; }

    internal BoundOutput(StructuredTask<T> task, OutputBindingOptions? options)
    {
        Task = task;
        _vocabularies = SchemaWriter.ResolveVocabularies(task.Contract, options?.Vocabularies, order: task.VocabularyOrder)
            .ToDictionary(x => x.Key, x => (IReadOnlyList<string>)Array.AsReadOnly(x.Value), StringComparer.Ordinal);
        _schema = task.CreateSchema(_vocabularies);
        Guidance = options?.OutputSpecification is { } specification ? task.CreateOutputSpecification(specification, _vocabularies) : null;
    }

    /// <summary>Gets the schema name.</summary>
    public string SchemaName => Task.SchemaName;
    /// <summary>Creates a detached strict schema.</summary>
    public JsonElement CreateSchema() => _schema.Clone();
    /// <summary>Validates and reads output using the captured vocabulary.</summary>
    public StructuredResult<T> ReadOutput(string json, StructuredMetadata? metadata = null) => Task.ReadOutput(json, _vocabularies, metadata);
}
