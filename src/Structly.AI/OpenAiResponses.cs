using System.Text.Json;

namespace Structly.AI.OpenAI;

public sealed partial class OpenAiClient
{
    /// <summary>Generates free text without an output schema, using the same response and streaming policies.</summary>
    public Task<StructuredResult<string>> GenerateTextAsync(TextRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Request);
        return RunOperation(request.Request, "Text", execution => TextCore(request, execution), cancellationToken);
    }

    async Task<StructuredResult<string>> TextCore(TextRequest textRequest, OpenAiExecution execution)
    {
        execution.CheckCancellation();
        var request = textRequest.Request;
        Dictionary<string, object?> payload;
        try
        {
            ValidateResponseRequest(request);
            if (request.OutputSpecification is not null || request.Vocabularies is not null) throw new ArgumentException("Text has no output contract.");
            var model = SelectModel(request.ModelSelection ?? _options.DefaultModel);
            execution.Metadata = execution.Metadata with { RequestedModel = model.ModelId };
            payload = ResponsePayload(request, model, textRequest.Instructions);
        }
        catch (ArgumentException) { return InvalidOptions<string>(execution); }
        return await Send(payload, "responses", request, execution, null,
            root => ProcessResponse(root, request, execution, (text, metadata) => String.IsNullOrWhiteSpace(text)
                ? LocalFailure<string>(execution, StructuredErrorKind.InvalidResponse)
                : StructuredResult<string>.Success(text, metadata))).ConfigureAwait(false);
    }

    /// <summary>Prewarms input without generating output. No cache-write or reuse guarantee.</summary>
    public Task<StructuredResult<OpenAiPrewarmResult>> PrewarmAsync(OpenAiPrewarmRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Request);
        return RunOperation(request.Request, "Prewarm", execution => PrewarmCore(request, execution, null, null), cancellationToken);
    }

    /// <summary>Prewarms with the same strict format and vocabulary as a future typed call.</summary>
    public Task<StructuredResult<OpenAiPrewarmResult>> PrewarmAsync<T>(StructuredTask<T> task, OpenAiPrewarmRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Request);
        return RunOperation(request.Request, "Prewarm", execution => PrewarmCore(request, execution, task.CredentialResolver, () =>
        {
            if (request.Instructions is not null) throw new ArgumentException("Typed prewarm uses task instructions.");
            var model = SelectModel(request.Request.ModelSelection ?? task.ModelSelection ?? _options.DefaultModel);
            execution.Metadata = execution.Metadata with { RequestedModel = model.ModelId };
            var payload = ResponsePayload(request.Request, model, task.Instructions, prewarm: true);
            var format = new Dictionary<string, object?>
            {
                ["type"] = "json_schema",
                ["name"] = task.SchemaName,
                ["schema"] = task.CreateSchema(request.Request.Vocabularies),
                ["strict"] = true
            };
            if (task.Description is not null) format["description"] = task.Description;
            payload["text"] = new { format };
            return payload;
        }), cancellationToken);
    }

    async Task<StructuredResult<OpenAiPrewarmResult>> PrewarmCore(OpenAiPrewarmRequest warm, OpenAiExecution execution,
        Func<CancellationToken, ValueTask<string?>>? taskCredential, Func<Dictionary<string, object?>>? typedPayload)
    {
        execution.CheckCancellation();
        var request = warm.Request;
        Dictionary<string, object?> payload;
        try
        {
            ValidateResponseRequest(request, true);
            if (typedPayload is null)
            {
                if (request.Vocabularies is not null) throw new ArgumentException("Untyped prewarm has no vocabularies.");
                var model = SelectModel(request.ModelSelection ?? _options.DefaultModel);
                execution.Metadata = execution.Metadata with { RequestedModel = model.ModelId };
                payload = ResponsePayload(request, model, warm.Instructions, prewarm: true);
            }
            else payload = typedPayload();
        }
        catch (StructuredSchemaException exception) { return LocalFailure<OpenAiPrewarmResult>(execution, StructuredErrorKind.UnsupportedSchema, issues: exception.Issues); }
        catch (ArgumentException) { return InvalidOptions<OpenAiPrewarmResult>(execution); }
        return await Send(payload, "responses", request, execution, taskCredential,
            root => ProcessResponse(root, request, execution,
                (_, _) => throw new InvalidOperationException("Prewarm cannot process generated output."), true, new OpenAiPrewarmResult())).ConfigureAwait(false);
    }

    static void ValidateCommonRequest(StructuredRequest request)
    {
        if (request.OpenAi is null || request.TotalTimeout is { } total && !OpenAiExecution.ValidTimeout(total))
            throw new ArgumentException("Invalid deadlines/provider options.");
        if (request.OpenAi.IdempotencyKey is { } key && (String.IsNullOrWhiteSpace(key) || key.Any(c => c < 32 || c > 126)))
            throw new ArgumentException("Invalid idempotency header.");
        if (request.OpenAi.Metadata is { } metadata && (metadata.Count > 16 || metadata.Any(x =>
            String.IsNullOrWhiteSpace(x.Key) || x.Key.Length > 64 || x.Value is null || x.Value.Length > 512)))
            throw new ArgumentException("Invalid provider metadata.");
    }

    static void ValidateResponseRequest(StructuredRequest request, bool prewarm = false)
    {
        ValidateCommonRequest(request);
        if (request.MaxOutputTokens is <= 0 || request.InactivityTimeout is { } idle && idle <= TimeSpan.Zero ||
            !request.Stream && (request.InactivityTimeout is not null || request.Progress is not null || request.IncludeReasoningSummary))
            throw new ArgumentException("Invalid response controls.");
        if (prewarm && (request.Stream || request.Progress is not null || request.IncludeReasoningSummary || request.MaxOutputTokens is not null ||
            request.OpenAi.PreviousResponseId is not null || request.OutputSpecification is not null || request.OpenAi.CaptureOutputText || request.OpenAi.Store))
            throw new ArgumentException("Prewarm does not generate output or continue/store a conversation.");
        if (request.OpenAi.PreviousResponseId is { } previous && String.IsNullOrWhiteSpace(previous) ||
            request.OpenAi.PromptCacheKey is { } cacheKey && String.IsNullOrWhiteSpace(cacheKey))
            throw new ArgumentException("Response/cache IDs must be nonblank.");
    }

    Dictionary<string, object?> ResponsePayload(StructuredRequest request, ModelSelection model, string? instructions,
        string? guidance = null, bool prewarm = false)
    {
        if (instructions is not null && String.IsNullOrWhiteSpace(instructions)) throw new ArgumentException("Instructions must be nonblank.");
        var (input, breakpoints) = CreateInput(request, guidance);
        var payload = new Dictionary<string, object?>
        {
            ["model"] = model.ModelId,
            ["input"] = input,
            ["store"] = request.OpenAi.Store,
            ["stream"] = request.Stream
        };
        if (instructions is not null) payload["instructions"] = instructions;
        if (request.MaxOutputTokens is { } cap) payload["max_output_tokens"] = cap;
        if (model.ReasoningEffort is not null || request.IncludeReasoningSummary)
        {
            var reasoning = new Dictionary<string, object?>();
            if (model.ReasoningEffort is { } effort) reasoning["effort"] = effort.ToString().ToLowerInvariant();
            if (request.IncludeReasoningSummary) reasoning["summary"] = "auto";
            payload["reasoning"] = reasoning;
        }
        if (request.OpenAi.Metadata is { } metadata) payload["metadata"] = metadata.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        if (request.OpenAi.PreviousResponseId is { } previous) payload["previous_response_id"] = previous;
        if (request.OpenAi.PromptCacheKey is { } key) payload["prompt_cache_key"] = key;
        ApplyCache(payload, request.OpenAi, model.ModelId!, breakpoints, prewarm);
        return payload;
    }

    static (object Input, int Breakpoints) CreateInput(StructuredRequest request, string? guidance)
    {
        if ((request.Input is null) == (request.Messages is null) || request.Input is { } input && String.IsNullOrWhiteSpace(input))
            throw new ArgumentException("Supply exactly one of nonblank Input or nonempty Messages.");
        // String shorthand is the provider's equivalent of one user text message.
        if (request.Input is { } shorthand && String.IsNullOrEmpty(guidance)) return (shorthand, 0);
        var source = request.Messages?.ToArray() ?? [new(MessageRole.User, [new TextPart(request.Input!)])];
        if (source.Length == 0) throw new ArgumentException("Messages must be nonempty.");
        var messages = new List<Dictionary<string, object?>>();
        var breakpoints = 0;
        List<Dictionary<string, object?>>? lastUser = null;
        foreach (var message in source)
        {
            if (message is null || !Enum.IsDefined(message.Role) || message.Content is null) throw new ArgumentException("Invalid message.");
            var content = new List<Dictionary<string, object?>>();
            foreach (var part in message.Content.ToArray())
            {
                Dictionary<string, object?> block;
                if (part is TextPart text && !String.IsNullOrWhiteSpace(text.Text))
                    block = new() { ["type"] = message.Role == MessageRole.Assistant ? "output_text" : "input_text", ["text"] = text.Text };
                else if (part is ImagePart image && message.Role == MessageRole.User && Enum.IsDefined(image.Detail) && ValidImageUrl(image.Url))
                    block = new() { ["type"] = "input_image", ["image_url"] = image.Url, ["detail"] = image.Detail.ToString().ToLowerInvariant() };
                else throw new ArgumentException("Invalid content: use nonblank text or user HTTPS/base64 images.");
                if (part.CacheBreakpoint)
                {
                    if (message.Role == MessageRole.Assistant) throw new ArgumentException("Assistant output cannot mark a cache breakpoint.");
                    breakpoints++;
                    block["prompt_cache_breakpoint"] = new { mode = "explicit" };
                }
                content.Add(block);
            }
            if (content.Count == 0) throw new ArgumentException("Message content must be nonempty.");
            if (message.Role == MessageRole.User) lastUser = content;
            messages.Add(new() { ["role"] = message.Role.ToString().ToLowerInvariant(), ["content"] = content });
        }
        if (!String.IsNullOrEmpty(guidance))
        {
            if (lastUser is null) throw new ArgumentException("Output guidance requires a user message.");
            lastUser.Add(new() { ["type"] = "input_text", ["text"] = guidance });
        }
        return (messages, breakpoints);
    }

    static bool ValidImageUrl(string? url)
    {
        if (url is null) return false;
        if (url.StartsWith("data:", StringComparison.Ordinal))
        {
            var comma = url.IndexOf(',');
            if (comma < 0 || url[..comma] is not ("data:image/png;base64" or "data:image/jpeg;base64" or "data:image/webp;base64" or "data:image/gif;base64")) return false;
            try { return Convert.FromBase64String(url[(comma + 1)..]).Length > 0; }
            catch (FormatException) { return false; }
        }
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.Host.Length > 0 &&
            uri.UserInfo.Length == 0 && uri.Fragment.Length == 0;
    }

    void ApplyCache(Dictionary<string, object?> payload, OpenAiResponseOptions options, string model, int breakpoints, bool prewarm)
    {
        var modern = options.Cache is not null || breakpoints != 0 || prewarm;
        if (!modern && options.CacheRetention is null) return;
        if (!_options.CacheCompatibility.TryGetValue(model, out var compatibility)) throw new ArgumentException("Configure model cache compatibility.");
        if (modern)
        {
            var cache = options.Cache;
            if (compatibility != OpenAiCacheCompatibility.Modern || options.CacheRetention is not null ||
                cache?.Mode is { } mode && !Enum.IsDefined(mode) || cache?.Ttl is { } ttl && ttl != "30m" ||
                cache?.ComparisonResponseId is { } comparison && String.IsNullOrWhiteSpace(comparison) ||
                cache?.Mode == OpenAiCacheMode.Explicit && breakpoints == 0 ||
                breakpoints > (cache?.Mode == OpenAiCacheMode.Explicit ? 4 : 3))
                throw new ArgumentException("Invalid modern cache controls or breakpoint count.");
            var wire = new Dictionary<string, object?>();
            if (cache?.Mode is { } selectedMode) wire["mode"] = selectedMode.ToString().ToLowerInvariant();
            if (cache?.Ttl is { } selectedTtl) wire["ttl"] = selectedTtl;
            if (cache?.ComparisonResponseId is { } selectedComparison) wire["comparison_response_id"] = selectedComparison;
            if (prewarm) wire["prewarm"] = true;
            payload["prompt_cache_options"] = wire;
        }
        else
        {
            if (compatibility != OpenAiCacheCompatibility.Legacy || !Enum.IsDefined(options.CacheRetention!.Value)) throw new ArgumentException("Invalid legacy retention.");
            payload["prompt_cache_retention"] = options.CacheRetention == OpenAiCacheRetention.InMemory ? "in_memory" : "24h";
        }
    }

    static CacheDiagnostics? ParseCacheDiagnostics(JsonElement root, List<StructuredWarning> warnings)
    {
        var diagnostic = Property(root, "prompt_cache_diagnostics");
        if (diagnostic.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return null;
        if (diagnostic.ValueKind != JsonValueKind.Object) { warnings.Add(new("InvalidCacheDiagnostics", "Provider cache diagnostics are malformed.")); return null; }
        long? Count(string name)
        {
            var value = Property(diagnostic, name);
            if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return null;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var count) && count >= 0) return count;
            warnings.Add(new("InvalidCacheDiagnostics", "Provider cache diagnostic count is invalid."));
            return null;
        }
        return new()
        {
            Type = Text(diagnostic, "type"),
            Reason = Text(diagnostic, "reason"),
            ComparisonReusableTokens = Count("comparison_reusable_tokens"),
            CacheMissedTokens = Count("cache_missed_tokens")
        };
    }
}
