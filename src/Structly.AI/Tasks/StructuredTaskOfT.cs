using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;

namespace Structly.AI;

/// <summary>An immutable typed contract, safe for concurrent schema and output inspection.</summary>
public sealed partial class StructuredTask<T>
{
    readonly SchemaNode _contract;
    internal SchemaNode Contract => _contract;

    /// <summary>Snapshots and validates runtime output settings without resolving credentials.</summary>
    public BoundOutput<T> BindOutput(OutputBindingOptions? options = null) => new(this, options);
    readonly JsonSerializerOptions _serializer;

    internal StructuredTask(StructuredTaskOptions options)
    {
        if(options.Instructions is not null && String.IsNullOrWhiteSpace(options.Instructions))
            throw new ArgumentException("Task instructions must be nonblank.", nameof(options));

        ArgumentNullException.ThrowIfNull(options.SerializationProfile);
        if(!Enum.IsDefined(options.SerializationProfile.Naming))
            throw new ArgumentException("Unsupported naming policy.", nameof(options));

        options.ModelSelection?.Validate();
        if(!Enum.IsDefined(options.VocabularyOrder))
            throw new ArgumentException("Invalid vocabulary order.", nameof(options));

        VocabularyOrder = options.VocabularyOrder;
        ModelSelection = options.ModelSelection;
        CredentialResolver = options.CredentialResolver;
        Instructions = options.Instructions;
        SerializationProfile = options.SerializationProfile with { };
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            // Presence is enforced for every property by the resolved contract.
            // STJ otherwise rejects JsonRequired on constructor-bound get-only members.
            foreach(var property in info.Properties)
                property.IsRequired = false;
        });
        _serializer = new JsonSerializerOptions
        {
            PropertyNamingPolicy = SerializationProfile.Naming == PropertyNaming.CamelCase ? JsonNamingPolicy.CamelCase : null,
            PropertyNameCaseInsensitive = false,
            TypeInfoResolver = resolver,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            MaxDepth = 64,
        };
        _serializer.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        _serializer.MakeReadOnly();
        _contract = SchemaResolver.Resolve(typeof(T), _serializer);
        var attribute = typeof(T).GetCustomAttributes(typeof(SchemaAttribute), true).Cast<SchemaAttribute>().SingleOrDefault();
        var name = options.SchemaName ?? attribute?.Name;
        if(name is null)
        {
            name = Regex.Replace(typeof(T).Name, "[^A-Za-z0-9_-]", "_");
            name = name[..Math.Min(name.Length, 64)];
        }

        if(!Regex.IsMatch(name, "\\A[A-Za-z0-9_-]{1,64}\\z"))
            throw new ArgumentException("SchemaName must contain 1–64 ASCII letters, digits, underscores or hyphens.", nameof(options));

        SchemaName = name;
        Description = options.Description ?? _contract.Description;
        if(Description is not null && String.IsNullOrWhiteSpace(Description))
            throw new ArgumentException("Description must be nonblank when supplied.", nameof(options));

        SchemaWriter.Create(_contract, Description, null, allowMissingVocabularies: true);
    }

    /// <summary>Gets vocabulary ordering.</summary>
    public VocabularyOrder VocabularyOrder { get; }
    /// <summary>Gets task-specific model selection.</summary>
    public ModelSelection? ModelSelection { get; }
    /// <summary>Gets task-specific credentials.</summary>
    public Func<CancellationToken, ValueTask<string?>>? CredentialResolver { get; }

    /// <summary>Gets immutable task instructions.</summary>
    public string? Instructions { get; }
    /// <summary>Gets the resolved valid provider schema name.</summary>
    public string SchemaName { get; }
    /// <summary>Gets the resolved schema description.</summary>
    public string? Description { get; }
    /// <summary>Gets immutable serialization settings.</summary>
    public SerializationProfile SerializationProfile { get; }

    /// <summary>Creates a detached deterministic strict schema using snapshotted vocabularies.</summary>
    public JsonElement CreateSchema(IReadOnlyDictionary<string, IReadOnlyList<string>>? vocabularies = null)
        => SchemaWriter.Create(_contract, Description, vocabularies, order: VocabularyOrder);

    /// <summary>Validates JSON before deserializing. Results retain supplied metadata values and include this task's schema name.</summary>
    public StructuredResult<T> ReadOutput(string json,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? vocabularies = null,
        StructuredMetadata? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(json);
        metadata = (metadata ?? new StructuredMetadata { Operation = "ValidateOutput" }) with { SchemaName = SchemaName };
        Dictionary<string, string[]> values;
        try
        {
            values = SchemaWriter.ResolveVocabularies(_contract, vocabularies, order: VocabularyOrder);
            SchemaWriter.Create(_contract, Description, values.ToDictionary(x => x.Key, x => (IReadOnlyList<string>)x.Value), order: VocabularyOrder);
        }
        catch(StructuredSchemaException exception)
        {
            return StructuredResult<T>.Failure(new StructuredError
            {
                Kind = StructuredErrorKind.UnsupportedSchema,
                Message = "The schema or vocabulary is invalid.",
                Issues = exception.Issues,
            }, metadata);
        }

        if(System.Text.Encoding.UTF8.GetByteCount(json) > 16 * 1024 * 1024)
            return Invalid([new("$", "OutputSize", "Output JSON exceeds 16 MiB.")], metadata);

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
            var issues = OutputValidator.Validate(_contract, document.RootElement, values, _serializer);
            if(issues.Count > 0)
                return Invalid(issues, metadata);

            var value = JsonSerializer.Deserialize<T>(document.RootElement, _serializer);
            return value is null ? Invalid([new("$", "NullRoot", "The output root must be an object.")], metadata)
                : StructuredResult<T>.Success(value, metadata);
        }
        catch(Exception exception) when(exception is not OutOfMemoryException and not StackOverflowException and not AccessViolationException)
        {
            // Host materialization cancellation is an output failure. The execution
            // layer independently checks actual caller cancellation and deadlines.
            return Invalid([new("$", "Deserialization", "Output must be valid JSON constructible as the declared DTO.")], metadata);
        }
    }

    static string IssueSummary(IReadOnlyList<StructuredIssue> issues)
    {
        var summary = "Output does not satisfy the typed contract.";
        var included = 0;
        foreach(var issue in issues.Take(5))
        {
            var pair = " " + JsonSerializer.Serialize(issue.Path) + "/" + JsonSerializer.Serialize(issue.Code) + ";";
            if(summary.Length + pair.Length > 480)
                break;

            summary += pair;
            included++;
        }

        if(included < issues.Count)
            summary += " (additional issues omitted)";

        return summary;
    }

    static StructuredResult<T> Invalid(IReadOnlyList<StructuredIssue> issues, StructuredMetadata metadata)
        => StructuredResult<T>.Failure(new StructuredError
        {
            Kind = StructuredErrorKind.InvalidOutput,
            Message = IssueSummary(issues),
            Issues = issues,
        }, metadata);
}
