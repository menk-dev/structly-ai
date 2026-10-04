using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Structly.AI.OpenAI;

/// <summary>One-attempt OpenAI response, embedding and image client. Does not own or mutate its HttpClient.</summary>
public sealed partial class OpenAiClient
{
    readonly HttpClient _http;
    readonly OpenAiClientOptions _options;
    readonly Dictionary<string, ModelSelection> _profiles;

    /// <summary>Snapshots and validates configuration without resolving credentials.</summary>
    public OpenAiClient(HttpClient httpClient, OpenAiClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.DefaultModel);
        ArgumentNullException.ThrowIfNull(options.Profiles);
        options.DefaultModel.Validate();
        if (options.TimeProvider is null || !OpenAiExecution.ValidTimeout(options.TotalTimeout) ||
            options.InactivityTimeout is { } idle && idle <= TimeSpan.Zero)
            throw new ArgumentException("Invalid execution clock or deadlines.", nameof(options));
        if (options.BaseAddress is null || !options.BaseAddress.IsAbsoluteUri ||
            options.BaseAddress.Scheme is not ("https" or "http") || !options.BaseAddress.AbsolutePath.EndsWith('/') ||
            options.BaseAddress.Query.Length != 0 || options.BaseAddress.Fragment.Length != 0 || options.BaseAddress.UserInfo.Length != 0 ||
            options.MaxResponseBytes <= 0 || options.MaxImageResponseBytes <= 0)
            throw new ArgumentException("Invalid API directory URI or response size ceiling.", nameof(options));
        _profiles = new(options.Profiles, StringComparer.Ordinal);
        foreach (var (name, profile) in _profiles)
        {
            if (String.IsNullOrWhiteSpace(name) || profile is null) throw new ArgumentException("Invalid model profile.", nameof(options));
            profile.Validate();
            if (profile.ModelId is null) throw new ArgumentException("Profiles must select explicit model IDs.", nameof(options));
        }
        SelectModel(options.DefaultModel);
        var embeddingProfiles = SnapshotProfiles(options.EmbeddingProfiles);
        var imageProfiles = SnapshotProfiles(options.ImageProfiles);
        var compatibility = new Dictionary<string, OpenAiCacheCompatibility>(options.CacheCompatibility, StringComparer.Ordinal);
        if (compatibility.Any(x => String.IsNullOrWhiteSpace(x.Key) || !Enum.IsDefined(x.Value)))
            throw new ArgumentException("Invalid cache compatibility configuration.", nameof(options));
        _http = httpClient;
        _options = options with
        {
            Profiles = new System.Collections.ObjectModel.ReadOnlyDictionary<string, ModelSelection>(_profiles),
            EmbeddingProfiles = new System.Collections.ObjectModel.ReadOnlyDictionary<string, ModelSelection>(embeddingProfiles),
            ImageProfiles = new System.Collections.ObjectModel.ReadOnlyDictionary<string, ModelSelection>(imageProfiles),
            CacheCompatibility = new System.Collections.ObjectModel.ReadOnlyDictionary<string, OpenAiCacheCompatibility>(compatibility)
        };
    }

    /// <summary>Validates locally, sends one request and validates the returned typed output.</summary>
    public Task<StructuredResult<T>> ExecuteAsync<T>(StructuredTask<T> task, StructuredRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(request);
        return RunOperation(request, "Structured", execution => ExecuteCore(task, request, execution), cancellationToken, task.SchemaName);
    }

    async Task<StructuredResult<T>> RunOperation<T>(StructuredRequest request, string operation,
        Func<OpenAiExecution, Task<StructuredResult<T>>> core, CancellationToken cancellationToken, string? schemaName = null)
    {
        using var execution = new OpenAiExecution(request, _options, cancellationToken);
        execution.Metadata = execution.Metadata with { Operation = operation, SchemaName = schemaName };
        StructuredResult<T> result;
        try { result = await core(execution).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            result = LocalFailure<T>(execution, execution.Deadline ?? StructuredErrorKind.TransportFailure, true);
        }
        if (execution.Deadline is { } deadline) result = LocalFailure<T>(execution, deadline, true);
        if (execution.Metadata.Usage is not null && (request.UsageObserver ?? _options.UsageObserver) is { } observer)
        {
            var usage = new StructuredUsageEvent
            {
                Metadata = execution.Metadata,
                Succeeded = result.IsSuccess && !cancellationToken.IsCancellationRequested,
                FailureKind = result.Error?.Kind,
                CallerCancelled = cancellationToken.IsCancellationRequested
            };
            await execution.Callback(token => observer(usage, token), TimeSpan.FromSeconds(5), "UsageObserver").ConfigureAwait(false);
        }
        if (cancellationToken.IsCancellationRequested)
            throw new StructuredOperationCanceledException(execution.Metadata, execution.Warnings, cancellationToken);
        if (execution.Deadline is { } finalDeadline) return LocalFailure<T>(execution, finalDeadline, true);
        return result.IsSuccess ? StructuredResult<T>.Success(result.Value!, execution.Metadata, execution.Warnings)
            : StructuredResult<T>.Failure(result.Error!, execution.Metadata, execution.Warnings);
    }

    static StructuredResult<T> LocalFailure<T>(OpenAiExecution execution, StructuredErrorKind kind, bool transient = false,
        IReadOnlyList<StructuredIssue>? issues = null) => StructuredResult<T>.Failure(new StructuredError
        { Kind = kind, Message = $"Structured operation failed: {kind}.", IsTransient = transient, Issues = issues ?? [] },
        execution.Metadata, execution.Warnings);

    async Task<StructuredResult<T>> ExecuteCore<T>(StructuredTask<T> task, StructuredRequest request, OpenAiExecution execution)
    {
        execution.CheckCancellation();
        Dictionary<string, IReadOnlyList<string>>? vocabularies;
        Dictionary<string, object?> payload;
        try
        {
            ValidateResponseRequest(request);
            vocabularies = request.Vocabularies?.ToDictionary(x => x.Key,
                x => (IReadOnlyList<string>)(x.Value ?? throw new ArgumentException("Vocabulary values cannot be null.")).ToArray(), StringComparer.Ordinal);
            var schema = task.CreateSchema(vocabularies);
            var guidance = request.OutputSpecification is { } specification ? task.CreateOutputSpecification(specification, vocabularies) : null;
            var model = SelectModel(request.ModelSelection ?? task.ModelSelection ?? _options.DefaultModel);
            execution.Metadata = execution.Metadata with { RequestedModel = model.ModelId };
            payload = ResponsePayload(request, model, request.Instructions ?? task.Instructions, guidance);
            var format = new Dictionary<string, object?> { ["type"] = "json_schema", ["name"] = task.SchemaName, ["schema"] = schema, ["strict"] = true };
            if (task.Description is not null) format["description"] = task.Description;
            payload["text"] = new { format };
        }
        catch (StructuredSchemaException exception) { return LocalFailure<T>(execution, StructuredErrorKind.UnsupportedSchema, issues: exception.Issues); }
        catch (ArgumentException) { return InvalidOptions<T>(execution); }
        return await Send(payload, "responses", request, execution, task.CredentialResolver,
            root => ProcessResponse(root, request, execution, (text, metadata) => task.ReadOutput(text, vocabularies, metadata))).ConfigureAwait(false);
    }

    static StructuredResult<T> InvalidOptions<T>(OpenAiExecution execution) => LocalFailure<T>(execution, StructuredErrorKind.InvalidRequest,
        issues: [new("$", "RequestOptions", "Check input, model/profile, deadlines and provider options. Output guidance requires a valid ExampleJson or IncludeExample=false when a sample cannot be generated.")]);

    static Dictionary<string, ModelSelection> SnapshotProfiles(IReadOnlyDictionary<string, ModelSelection> profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        var snapshot = new Dictionary<string, ModelSelection>(profiles, StringComparer.Ordinal);
        foreach (var (name, profile) in snapshot)
        {
            if (String.IsNullOrWhiteSpace(name) || profile is null) throw new ArgumentException("Invalid model profile.");
            profile.Validate();
            if (profile.ModelId is null || profile.ReasoningEffort is not null) throw new ArgumentException("Auxiliary profiles require explicit model IDs without reasoning.");
        }
        return snapshot;
    }

    ModelSelection SelectModel(ModelSelection selection, IReadOnlyDictionary<string, ModelSelection>? profiles = null)
    {
        selection.Validate();
        if (selection.ProfileName is not { } name) return selection;
        if (!(profiles ?? _profiles).TryGetValue(name, out var profile)) throw new ArgumentException("Unknown model profile.");
        return profile with { ReasoningEffort = selection.ReasoningEffort ?? profile.ReasoningEffort };
    }

    async Task<StructuredResult<T>> Send<T>(Dictionary<string, object?> payload, string endpoint,
        StructuredRequest request, OpenAiExecution execution,
        Func<CancellationToken, ValueTask<string?>>? taskCredential,
        Func<JsonElement, ValueTask<StructuredResult<T>>> process)
    {
        var cancellationToken = execution.Token;
        var warnings = execution.Warnings;
        StructuredResult<T> Fail(StructuredErrorKind kind, bool transient = false, HttpStatusCode? status = null, TimeSpan? retry = null) =>
            StructuredResult<T>.Failure(new StructuredError
            {
                Kind = kind,
                Message = $"Structured operation failed: {kind}.",
                IsTransient = transient,
                HttpStatusCode = status,
                RetryAfter = retry
            }, execution.Metadata, warnings);
        execution.CheckCancellation();
        string? credential;
        try
        {
            var resolver = request.CredentialResolver ?? taskCredential ?? _options.CredentialResolver;
            credential = resolver is null ? null : await execution.Await(resolver(cancellationToken).AsTask()).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        { return Fail(StructuredErrorKind.Authentication); }
        if (String.IsNullOrWhiteSpace(credential) || credential.Any(c => c < 33 || c > 126)) return Fail(StructuredErrorKind.Authentication);
        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(_options.BaseAddress, endpoint));
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);
        if (request.OpenAi.IdempotencyKey is { } idempotency) message.Headers.Add("Idempotency-Key", idempotency);
        message.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        execution.CheckCancellation();
        await execution.Progress(new() { Kind = StructuredProgressKind.Started }).ConfigureAwait(false);
        execution.CheckCancellation();
        try
        {
            using var response = await execution.Await(_http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken), value => value.Dispose()).ConfigureAwait(false);
            execution.Metadata = execution.Metadata with { ProviderRequestId = response.Headers.TryGetValues("x-request-id", out var ids) ? ids.FirstOrDefault() : null };
            using var stream = await execution.Await(response.Content.ReadAsStreamAsync(cancellationToken), value => value.Dispose()).ConfigureAwait(false);
            JsonDocument document;
            try
            {
                if (request.Stream && response.IsSuccessStatusCode)
                {
                    if (response.Content.Headers.ContentType?.MediaType != "text/event-stream") return Fail(StructuredErrorKind.InvalidResponse);
                    document = await ReadEvents(stream, request, execution).ConfigureAwait(false);
                }
                else
                {
                    var limit = endpoint == "images/generations" ? _options.MaxImageResponseBytes : _options.MaxResponseBytes;
                    using var buffer = new MemoryStream();
                    var chunk = new byte[8192];
                    int count;
                    while ((count = await execution.Await(stream.ReadAsync(chunk, cancellationToken).AsTask()).ConfigureAwait(false)) != 0)
                    {
                        if (buffer.Length + count > limit)
                            return response.IsSuccessStatusCode ? Fail(StructuredErrorKind.InvalidResponse) : HttpFailure(null);
                        buffer.Write(chunk, 0, count);
                    }
                    document = JsonDocument.Parse(buffer.ToArray());
                }
            }
            catch (JsonException) { return response.IsSuccessStatusCode ? Fail(StructuredErrorKind.InvalidResponse) : HttpFailure(null); }
            using (document)
            {
                var root = document.RootElement;
                execution.Metadata = execution.Metadata with
                {
                    ResponseId = Text(root, "id") ?? execution.Metadata.ResponseId,
                    ResolvedModel = Text(root, "model") ?? execution.Metadata.ResolvedModel,
                    RawResponse = request.OpenAi.CaptureRawResponse ? root : null,
                    Usage = ParseUsage(root, warnings),
                    CacheDiagnostics = ParseCacheDiagnostics(root, warnings)
                };
                if (!response.IsSuccessStatusCode) return HttpFailure(Text(Property(root, "error"), "code"));
                execution.CheckCancellation();
                var result = await process(root).ConfigureAwait(false);
                execution.CheckCancellation();
                return result;
            }

            StructuredResult<T> HttpFailure(string? code)
            {
                var status = (int)response.StatusCode;
                var kind = status switch
                {
                    401 => StructuredErrorKind.Authentication,
                    403 => StructuredErrorKind.PermissionDenied,
                    429 => StructuredErrorKind.RateLimited,
                    408 or >= 500 and <= 599 => StructuredErrorKind.ProviderUnavailable,
                    _ => StructuredErrorKind.ProviderRejected
                };
                var retry = response.Headers.RetryAfter;
                var delay = retry?.Delta ?? (retry?.Date is { } date ? date - _options.TimeProvider.GetUtcNow() : (TimeSpan?)null);
                return Fail(kind, kind == StructuredErrorKind.ProviderUnavailable || kind == StructuredErrorKind.RateLimited && code != "insufficient_quota",
                    response.StatusCode, delay < TimeSpan.Zero ? TimeSpan.Zero : delay);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return Fail(StructuredErrorKind.TransportFailure, true); }
        catch (HttpRequestException) { return Fail(StructuredErrorKind.TransportFailure, true); }
        catch (IOException) { return Fail(StructuredErrorKind.TransportFailure, true); }
    }

    static async ValueTask<StructuredResult<T>> ProcessResponse<T>(JsonElement root, StructuredRequest request,
        OpenAiExecution execution, Func<string, StructuredMetadata, StructuredResult<T>> readOutput,
        bool prewarm = false, T? prewarmValue = default)
    {
        StructuredResult<T> Fail(StructuredErrorKind kind, bool transient = false) => LocalFailure<T>(execution, kind, transient);
        if (root.ValueKind != JsonValueKind.Object || String.IsNullOrWhiteSpace(Text(root, "id")) || String.IsNullOrWhiteSpace(Text(root, "model")))
            return Fail(StructuredErrorKind.InvalidResponse);
        var status = Text(root, "status");
        if (prewarm)
        {
            if (ProviderStatusFailure() is { } failure) return failure;
            var warmOutput = Property(root, "output");
            if (warmOutput.ValueKind != JsonValueKind.Array || warmOutput.GetArrayLength() != 0) return Fail(StructuredErrorKind.InvalidResponse);
            return StructuredResult<T>.Success(prewarmValue!, execution.Metadata, execution.Warnings);
        }
        var output = Property(root, "output");
        // Terminal provider status takes precedence even when no answer is present.
        if (output.ValueKind != JsonValueKind.Array)
            return ProviderStatusFailure() ?? Fail(StructuredErrorKind.InvalidResponse);
        var text = new StringBuilder();
        var answers = 0;
        var refused = false;
        var invalid = false;
        foreach (var item in output.EnumerateArray())
        {
            if (Text(item, "type") == "reasoning") continue;
            if (Text(item, "type") != "message" || Text(item, "role") != "assistant" || Text(item, "status") != "completed") { invalid = true; continue; }
            answers++;
            var content = Property(item, "content");
            if (content.ValueKind != JsonValueKind.Array) { invalid = true; continue; }
            foreach (var part in content.EnumerateArray())
            {
                if (Text(part, "type") == "refusal" && Text(part, "refusal") is not null) refused = true;
                else if (Text(part, "type") == "output_text" && Text(part, "text") is { } value) text.Append(value);
                else invalid = true;
            }
        }
        execution.Metadata = execution.Metadata with { OutputText = request.OpenAi.CaptureOutputText ? text.ToString() : null };
        if (ProviderStatusFailure() is { } statusFailure) return statusFailure;
        if (refused) return Fail(StructuredErrorKind.Refused);
        if (invalid || answers != 1 || text.Length == 0) return Fail(StructuredErrorKind.InvalidResponse);
        execution.CheckCancellation();
        var result = readOutput(text.ToString(), execution.Metadata);
        execution.CheckCancellation();
        if (result.IsSuccess || result.Error?.Kind == StructuredErrorKind.InvalidOutput)
            await execution.Progress(new() { Kind = StructuredProgressKind.Completed, ResponseId = execution.Metadata.ResponseId, ModelId = execution.Metadata.ResolvedModel }).ConfigureAwait(false);
        return result.IsSuccess ? StructuredResult<T>.Success(result.Value!, execution.Metadata, execution.Warnings) : StructuredResult<T>.Failure(result.Error!, execution.Metadata, execution.Warnings);

        StructuredResult<T>? ProviderStatusFailure()
        {
            if (status == "completed") return null;
            if (status == "incomplete") return Fail(StructuredErrorKind.IncompleteOutput);
            if (status != "failed") return Fail(StructuredErrorKind.InvalidResponse);
            var transient = Text(Property(root, "error"), "code") is "server_error" or "rate_limit_exceeded";
            return Fail(transient ? StructuredErrorKind.ProviderUnavailable : StructuredErrorKind.ProviderRejected, transient);
        }
    }
    static JsonElement Property(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : default;
    static string? Text(JsonElement element, string name) => Property(element, name) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

    static StructuredUsage? ParseUsage(JsonElement root, List<StructuredWarning> warnings)
    {
        var usage = Property(root, "usage");
        if (usage.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) return null;
        if (usage.ValueKind != JsonValueKind.Object) { warnings.Add(new("InvalidUsage", "Provider usage metadata is malformed.")); return null; }
        long? Count(JsonElement element, string name)
        {
            var value = Property(element, name);
            if (value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) return null;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) && number >= 0) return number;
            warnings.Add(new("InvalidUsage", "A provider usage count is invalid."));
            return null;
        }
        var input = Property(usage, "input_tokens_details");
        var output = Property(usage, "output_tokens_details");
        if (input.ValueKind is not (JsonValueKind.Object or JsonValueKind.Null or JsonValueKind.Undefined) ||
            output.ValueKind is not (JsonValueKind.Object or JsonValueKind.Null or JsonValueKind.Undefined))
            warnings.Add(new("InvalidUsage", "Provider usage details are malformed."));
        var result = new StructuredUsage
        {
            InputTokens = Count(usage, "input_tokens") ?? Count(usage, "prompt_tokens"),
            OutputTokens = Count(usage, "output_tokens"),
            TotalTokens = Count(usage, "total_tokens"),
            CachedInputTokens = Count(input, "cached_tokens"),
            CacheWriteTokens = Count(input, "cache_write_tokens"),
            ReasoningTokens = Count(output, "reasoning_tokens"),
            InputTextTokens = Count(input, "text_tokens"),
            InputImageTokens = Count(input, "image_tokens"),
            OutputImageTokens = Count(output, "image_tokens"),
            OutputTextTokens = Count(output, "text_tokens")
        };
        return result.InputTokens is not null || result.OutputTokens is not null || result.TotalTokens is not null ||
            result.CachedInputTokens is not null || result.CacheWriteTokens is not null || result.ReasoningTokens is not null || result.InputTextTokens is not null || result.InputImageTokens is not null || result.OutputImageTokens is not null || result.OutputTextTokens is not null ? result : null;
    }
}
