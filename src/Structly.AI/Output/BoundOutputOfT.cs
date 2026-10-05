using System.Text.Json;

namespace Structly.AI;

/// <summary>An immutable resolved output contract and optional generated guidance.</summary>
public sealed class BoundOutput<T>
{
    readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _vocabularies;
    readonly JsonElement _schema;
    internal StructuredTask<T> Task { get; }
    internal string? Guidance { get; }
    internal object Format { get; }

    internal BoundOutput(StructuredTask<T> task, OutputBindingOptions? options)
    {
        Task = task;
        _vocabularies = SchemaWriter.ResolveVocabularies(task.Contract, options?.Vocabularies, order: task.VocabularyOrder)
            .ToDictionary(x => x.Key, x => (IReadOnlyList<string>)Array.AsReadOnly(x.Value), StringComparer.Ordinal);
        _schema = task.CreateSchema(_vocabularies);
        Guidance = options?.OutputSpecification is { } specification ? task.CreateOutputSpecification(specification, _vocabularies) : null;
        var format = new Dictionary<string, object?> { ["type"] = "json_schema", ["name"] = task.SchemaName, ["schema"] = _schema, ["strict"] = true };
        if(task.Description is not null)
            format["description"] = task.Description;

        Format = format;
    }

    /// <summary>Gets the schema name.</summary>
    public string SchemaName => Task.SchemaName;
    /// <summary>Creates a detached strict schema.</summary>
    public JsonElement CreateSchema() => _schema.Clone();
    /// <summary>Validates and reads output using the captured vocabulary.</summary>
    public StructuredResult<T> ReadOutput(string json, StructuredMetadata? metadata = null) => Task.ReadOutput(json, _vocabularies, metadata);
}
