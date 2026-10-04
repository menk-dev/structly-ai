using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Structly.AI.OpenAI;

/// <summary>One-attempt OpenAI Responses client. Does not own or mutate its HttpClient.</summary>
public sealed class OpenAiClient
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
        if (options.BaseAddress is null || !options.BaseAddress.IsAbsoluteUri ||
            options.BaseAddress.Scheme is not ("https" or "http") || !options.BaseAddress.AbsolutePath.EndsWith('/') ||
            options.BaseAddress.Query.Length != 0 || options.BaseAddress.Fragment.Length != 0 || options.BaseAddress.UserInfo.Length != 0 ||
            options.MaxResponseBytes <= 0)
            throw new ArgumentException("Invalid API directory URI or response size ceiling.", nameof(options));
        _profiles = new(options.Profiles, StringComparer.Ordinal);
        foreach (var (name, profile) in _profiles)
        {
            if (String.IsNullOrWhiteSpace(name) || profile is null) throw new ArgumentException("Invalid model profile.", nameof(options));
            profile.Validate();
            if (profile.ModelId is null) throw new ArgumentException("Profiles must select explicit model IDs.", nameof(options));
        }
        SelectModel(options.DefaultModel);
        _http = httpClient;
        _options = options with { Profiles = new System.Collections.ObjectModel.ReadOnlyDictionary<string, ModelSelection>(_profiles) };
    }

    /// <summary>Validates locally, sends one request and validates the returned typed output.</summary>
    public async Task<StructuredResult<T>> ExecuteAsync<T>(StructuredTask<T> task, StructuredRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(request);
        var metadata = new StructuredMetadata { Operation = "Structured", Provider = "openai", CorrelationId = request.CorrelationId };
        var warnings = new List<StructuredWarning>();
        StructuredResult<T> Fail(StructuredErrorKind kind, bool transient = false, HttpStatusCode? status = null, TimeSpan? retry = null,
            IReadOnlyList<StructuredIssue>? issues = null) => StructuredResult<T>.Failure(new StructuredError
            {
                Kind = kind,
                Message = $"Structured operation failed: {kind}.",
                IsTransient = transient,
                HttpStatusCode = status,
                RetryAfter = retry,
                Issues = issues ?? []
            }, metadata, warnings);
        cancellationToken.ThrowIfCancellationRequested();
        Dictionary<string, IReadOnlyList<string>>? vocabularies;
        Dictionary<string, string>? providerMetadata;
        JsonElement schema;
        ModelSelection model;
        try
        {
            if (String.IsNullOrWhiteSpace(request.Input) || request.MaxOutputTokens is <= 0 || request.OpenAi is null)
                throw new ArgumentException("Invalid input or options.");
            var key = request.OpenAi.IdempotencyKey;
            if (key is not null && (String.IsNullOrWhiteSpace(key) || key.Any(c => c < 32 || c > 126)))
                throw new ArgumentException("Invalid idempotency header.");
            providerMetadata = request.OpenAi.Metadata?.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
            if (providerMetadata is not null && (providerMetadata.Count > 16 || providerMetadata.Any(x =>
                String.IsNullOrWhiteSpace(x.Key) || x.Key.Length > 64 || x.Value is null || x.Value.Length > 512)))
                throw new ArgumentException("Invalid provider metadata.");
            vocabularies = request.Vocabularies?.ToDictionary(x => x.Key, x => (IReadOnlyList<string>)(x.Value ?? throw new ArgumentException("Vocabulary values cannot be null.")).ToArray(), StringComparer.Ordinal);
            schema = task.CreateSchema(vocabularies);
            model = SelectModel(request.ModelSelection ?? task.ModelSelection ?? _options.DefaultModel);
            metadata = metadata with { RequestedModel = model.ModelId };
        }
        catch (StructuredSchemaException exception) { return Fail(StructuredErrorKind.UnsupportedSchema, issues: exception.Issues); }
        catch (ArgumentException) { return Fail(StructuredErrorKind.InvalidRequest, issues: [new("$", "RequestOptions", "Check input, model selection, vocabularies and provider options.")]); }
        string? credential;
        try
        {
            var resolver = request.CredentialResolver ?? task.CredentialResolver ?? _options.CredentialResolver;
            credential = resolver is null ? null : await resolver(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        { return Fail(StructuredErrorKind.Authentication); }
        if (String.IsNullOrWhiteSpace(credential) || credential.Any(c => c < 33 || c > 126)) return Fail(StructuredErrorKind.Authentication);
        var format = new Dictionary<string, object?> { ["type"] = "json_schema", ["name"] = task.SchemaName, ["schema"] = schema, ["strict"] = true };
        if (task.Description is not null) format["description"] = task.Description;
        var payload = new Dictionary<string, object?>
        {
            ["model"] = model.ModelId,
            ["instructions"] = task.Instructions,
            ["input"] = request.Input,
            ["store"] = request.OpenAi.Store,
            ["stream"] = false,
            ["text"] = new { format }
        };
        if (request.MaxOutputTokens is { } cap) payload["max_output_tokens"] = cap;
        if (model.ReasoningEffort is { } effort) payload["reasoning"] = new { effort = effort.ToString().ToLowerInvariant() };
        if (providerMetadata is not null) payload["metadata"] = providerMetadata;
        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(_options.BaseAddress, "responses"));
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);
        if (request.OpenAi.IdempotencyKey is { } idempotency) message.Headers.Add("Idempotency-Key", idempotency);
        message.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        try
        {
            using var response = await _http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            metadata = metadata with { ProviderRequestId = response.Headers.TryGetValues("x-request-id", out var ids) ? ids.FirstOrDefault() : null };
            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            var chunk = new byte[8192];
            int count;
            while ((count = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) != 0)
            {
                if (buffer.Length + count > _options.MaxResponseBytes)
                    return response.IsSuccessStatusCode ? Fail(StructuredErrorKind.InvalidResponse) : HttpFailure(null);
                buffer.Write(chunk, 0, count);
            }
            JsonDocument document;
            try { document = JsonDocument.Parse(buffer.ToArray()); }
            catch (JsonException) { return response.IsSuccessStatusCode ? Fail(StructuredErrorKind.InvalidResponse) : HttpFailure(null); }
            using (document)
            {
                var root = document.RootElement;
                metadata = metadata with
                {
                    ResponseId = Text(root, "id"),
                    ResolvedModel = Text(root, "model"),
                    RawResponse = request.OpenAi.CaptureRawResponse ? root : null,
                    Usage = ParseUsage(root, warnings)
                };
                if (!response.IsSuccessStatusCode) return HttpFailure(Text(Property(root, "error"), "code"));
                if (root.ValueKind != JsonValueKind.Object || String.IsNullOrWhiteSpace(metadata.ResponseId) || String.IsNullOrWhiteSpace(metadata.ResolvedModel))
                    return Fail(StructuredErrorKind.InvalidResponse);
                var status = Text(root, "status");
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
                metadata = metadata with { OutputText = request.OpenAi.CaptureOutputText ? text.ToString() : null };
                if (ProviderStatusFailure() is { } statusFailure) return statusFailure;
                if (refused) return Fail(StructuredErrorKind.Refused);
                if (invalid || answers != 1 || text.Length == 0) return Fail(StructuredErrorKind.InvalidResponse);
                var result = task.ReadOutput(text.ToString(), vocabularies, metadata);
                return result.IsSuccess ? StructuredResult<T>.Success(result.Value!, metadata, warnings) : StructuredResult<T>.Failure(result.Error!, metadata, warnings);

                StructuredResult<T>? ProviderStatusFailure()
                {
                    if (status == "completed") return null;
                    if (status == "incomplete") return Fail(StructuredErrorKind.IncompleteOutput);
                    if (status != "failed") return Fail(StructuredErrorKind.InvalidResponse);
                    var transient = Text(Property(root, "error"), "code") is "server_error" or "rate_limit_exceeded";
                    return Fail(transient ? StructuredErrorKind.ProviderUnavailable : StructuredErrorKind.ProviderRejected, transient);
                }
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
                var delay = retry?.Delta ?? (retry?.Date is { } date ? date - DateTimeOffset.UtcNow : (TimeSpan?)null);
                return Fail(kind, kind == StructuredErrorKind.ProviderUnavailable || kind == StructuredErrorKind.RateLimited && code != "insufficient_quota",
                    response.StatusCode, delay < TimeSpan.Zero ? TimeSpan.Zero : delay);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return Fail(StructuredErrorKind.TransportFailure, true); }
        catch (HttpRequestException) { return Fail(StructuredErrorKind.TransportFailure, true); }
        catch (IOException) { return Fail(StructuredErrorKind.TransportFailure, true); }
    }

    ModelSelection SelectModel(ModelSelection selection)
    {
        selection.Validate();
        if (selection.ProfileName is not { } name) return selection;
        if (!_profiles.TryGetValue(name, out var profile)) throw new ArgumentException("Unknown model profile.");
        return profile with { ReasoningEffort = selection.ReasoningEffort ?? profile.ReasoningEffort };
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
            InputTokens = Count(usage, "input_tokens"),
            OutputTokens = Count(usage, "output_tokens"),
            TotalTokens = Count(usage, "total_tokens"),
            CachedInputTokens = Count(input, "cached_tokens"),
            CacheWriteTokens = Count(input, "cache_write_tokens"),
            ReasoningTokens = Count(output, "reasoning_tokens")
        };
        return result.InputTokens is not null || result.OutputTokens is not null || result.TotalTokens is not null ||
            result.CachedInputTokens is not null || result.CacheWriteTokens is not null || result.ReasoningTokens is not null ? result : null;
    }
}
