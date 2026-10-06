using System.Net.Http.Json;
using System.Text.Json;

namespace Structly.AI.Mistral;

/// <summary>Executes Mistral operations using a caller-owned HTTP client.</summary>
public sealed partial class MistralClient : IStructuredClient
{
    readonly HttpClient _http;
    readonly Uri _baseAddress;
    readonly ModelSelection _defaultModel;
    readonly Dictionary<string, ModelSelection> _profiles;
    readonly TimeSpan _totalTimeout;
    readonly TimeSpan? _inactivityTimeout;
    readonly TimeProvider _clock;
    readonly Dictionary<string, ModelSelection> _embeddingProfiles;
    readonly Dictionary<string, ModelSelection> _imageProfiles;
    readonly int _maxImageResponseBytes;
    readonly int _maxResponseBytes;
    readonly Func<CancellationToken, ValueTask<string?>>? _credentials;
    readonly Func<StructuredUsageEvent, CancellationToken, ValueTask>? _usageObserver;

    /// <summary>Validates and snapshots configuration without resolving credentials.</summary>
    public MistralClient(HttpClient httpClient, MistralOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.DefaultModel);
        ArgumentNullException.ThrowIfNull(options.Profiles);
        ArgumentNullException.ThrowIfNull(options.EmbeddingProfiles);
        ArgumentNullException.ThrowIfNull(options.ImageProfiles);
        ArgumentNullException.ThrowIfNull(options.TimeProvider);
        if(options.BaseAddress is not { IsAbsoluteUri: true } address || address.Scheme is not ("http" or "https") ||
            !address.AbsolutePath.EndsWith('/') || address.Query.Length > 0 || address.Fragment.Length > 0 || address.UserInfo.Length > 0 ||
            !ValidTimeout(options.TotalTimeout) || options.InactivityTimeout <= TimeSpan.Zero || options.MaxResponseBytes <= 0 || options.MaxImageResponseBytes <= 0)
            throw new ArgumentException("Invalid Mistral transport settings.", nameof(options));

        _profiles = SnapshotProfiles(options.Profiles, true);
        _embeddingProfiles = SnapshotProfiles(options.EmbeddingProfiles, false);
        _defaultModel = options.DefaultModel;
        _imageProfiles = SnapshotProfiles(options.ImageProfiles, false);
        _ = SelectModel(_defaultModel);
        _http = httpClient;
        _baseAddress = address;
        _totalTimeout = options.TotalTimeout;
        _inactivityTimeout = options.InactivityTimeout;
        _clock = options.TimeProvider;
        _maxResponseBytes = options.MaxResponseBytes;
        _maxImageResponseBytes = options.MaxImageResponseBytes;
        _credentials = options.CredentialResolver ?? (String.IsNullOrWhiteSpace(options.ApiKey)
            ? options.UseEnvironmentApiKey ? Credentials.FromEnvironment("MISTRAL_API_KEY") : null : Credentials.FromStatic(options.ApiKey));
        _usageObserver = options.UsageObserver;
    }

    /// <summary>Executes a reusable task with text input.</summary>
    public Task<StructuredResult<T>> ExecuteAsync<T>(StructuredTask<T> task, string input, CancellationToken cancellationToken = default)
        => ExecuteAsync(task, new StructuredRequest { Input = input }, cancellationToken);

    /// <summary>Executes a typed task with runtime output settings.</summary>
    public Task<StructuredResult<T>> ExecuteAsync<T>(StructuredTask<T> task, StructuredRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(request);
        request = task.ExecutionDefaults.Apply(request);
        return Run(request, task.ModelSelection, task.CredentialResolver, task.SchemaName,
            () => task.BindOutput(new() { Vocabularies = request.Vocabularies, OutputSpecification = request.OutputSpecification }),
            task.Instructions, cancellationToken);
    }

    /// <summary>Executes bound output without permitting output setting overrides.</summary>
    public Task<StructuredResult<T>> ExecuteAsync<T>(BoundOutput<T> output, StructuredRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(request);
        request = output.Task.ExecutionDefaults.Apply(request);
        return Run(request, output.Task.ModelSelection, output.Task.CredentialResolver, output.SchemaName,
            () => request.Vocabularies is not null || request.OutputSpecification is not null
                ? throw new ArgumentException("Bound output cannot be overridden.") : output,
            output.Task.Instructions, cancellationToken);
    }

    /// <summary>Generates free text without an output schema.</summary>
    public Task<StructuredResult<string>> GenerateTextAsync(TextRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Request);
        return Run<string>(request.Request, null, null, null, null, request.Instructions, cancellationToken);
    }

    Task<StructuredResult<T>> Run<T>(StructuredRequest request, ModelSelection? taskModel,
        Func<CancellationToken, ValueTask<string?>>? taskCredentials, string? schemaName,
        Func<BoundOutput<T>>? bind, string? instructions, CancellationToken caller)
        => RunOperation(request, bind is null ? "Text" : "Structured", async execution =>
        {
            var output = bind?.Invoke();
            var model = SelectModel(request.ModelSelection ?? taskModel ?? _defaultModel);
            execution.Metadata = execution.Metadata with { RequestedModel = model.ModelId };
            var payload = ChatPayload(request, model, request.Instructions ?? instructions, output);
            return await Send(execution, request, taskCredentials, HttpMethod.Post, "chat/completions", JsonContent.Create(payload),
                async response =>
                {
                    if(request.Stream)
                        return await ReadChatStream(response, request, output, execution).ConfigureAwait(false);

                    using var document = await ReadEnvelope(response, execution).ConfigureAwait(false);
                    RetainEnvelope(document.RootElement, request, execution);
                    return ReadChat(document.RootElement, request, output, execution);
                }).ConfigureAwait(false);
        }, caller, schemaName);

    static bool ValidTimeout(TimeSpan timeout) => timeout > TimeSpan.Zero && timeout <= TimeSpan.FromHours(24);

    static Dictionary<string, ModelSelection> SnapshotProfiles(Dictionary<string, ModelSelection> profiles, bool allowReasoning)
    {
        var snapshot = new Dictionary<string, ModelSelection>(profiles, StringComparer.Ordinal);
        foreach(var (name, model) in snapshot)
        {
            if(String.IsNullOrWhiteSpace(name) || model is null || model.ModelId is null || !allowReasoning && model.ReasoningEffort is not null)
                throw new ArgumentException("Profiles require a name and explicit model supporting the operation.");

            model.Validate();
        }

        return snapshot;
    }

    ModelSelection SelectModel(ModelSelection selection, Dictionary<string, ModelSelection>? profiles = null)
    {
        selection.Validate();
        if(selection.ProfileName is not { } profile)
            return selection;

        return (profiles ?? _profiles).TryGetValue(profile, out var model)
            ? model with { ReasoningEffort = selection.ReasoningEffort ?? model.ReasoningEffort }
            : throw new ArgumentException("Unknown model profile.");
    }

    static string? StringValue(JsonElement root, string name) => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    static StructuredUsage? ReadUsage(JsonElement root, List<StructuredWarning> warnings)
    {
        var usage = Property(root, "usage");
        if(usage.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;

        if(usage.ValueKind != JsonValueKind.Object)
        {
            warnings.Add(new("InvalidUsage", "Usage must be an object."));
            return null;
        }

        long? ReadCount(JsonElement value, string name)
        {
            var count = Property(value, name);
            if(count.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                return null;

            if(count.ValueKind == JsonValueKind.Number && count.TryGetInt64(out var number) && number >= 0)
                return number;

            warnings.Add(new("InvalidUsage", "Usage count must be a nonnegative integer."));
            return null;
        }

        var result = new StructuredUsage
        {
            InputTokens = ReadCount(usage, "prompt_tokens"),
            OutputTokens = ReadCount(usage, "completion_tokens"),
            TotalTokens = ReadCount(usage, "total_tokens"),
            CachedInputTokens = ReadCount(Property(usage, "prompt_tokens_details"), "cached_tokens"),
        };
        return result.InputTokens is null && result.OutputTokens is null && result.TotalTokens is null && result.CachedInputTokens is null ? null : result;
    }

    static long? Count(JsonElement root, string name) => Property(root, name) is { ValueKind: JsonValueKind.Number } count ? count.GetInt64() : null;
}
