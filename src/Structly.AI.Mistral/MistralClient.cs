using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Structly.AI.Mistral;

/// <summary>Executes non-streaming Mistral chat completions using a caller-owned HTTP client.</summary>
public sealed class MistralClient : IStructuredClient
{
    readonly HttpClient _http;
    readonly Uri _baseAddress;
    readonly ModelSelection _defaultModel;
    readonly Dictionary<string, ModelSelection> _profiles;
    readonly TimeSpan _totalTimeout;
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
        if(options.BaseAddress is not { IsAbsoluteUri: true } address || address.Scheme is not ("http" or "https") ||
            !address.AbsolutePath.EndsWith('/') || address.Query.Length > 0 || address.Fragment.Length > 0 || address.UserInfo.Length > 0 ||
            !ValidTimeout(options.TotalTimeout) || options.MaxResponseBytes <= 0)
            throw new ArgumentException("Invalid Mistral transport settings.", nameof(options));

        _profiles = new(options.Profiles, StringComparer.Ordinal);
        foreach(var (name, model) in _profiles)
        {
            if(String.IsNullOrWhiteSpace(name) || model is null || model.ModelId is null)
                throw new ArgumentException("Profiles require a name and explicit model.", nameof(options));

            ValidateModel(model);
        }

        _defaultModel = options.DefaultModel;
        _ = SelectModel(_defaultModel);
        _http = httpClient;
        _baseAddress = address;
        _totalTimeout = options.TotalTimeout;
        _maxResponseBytes = options.MaxResponseBytes;
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

    async Task<StructuredResult<T>> Run<T>(StructuredRequest request, ModelSelection? taskModel,
        Func<CancellationToken, ValueTask<string?>>? taskCredentials, string? schemaName,
        Func<BoundOutput<T>>? bind, string? instructions, CancellationToken caller)
    {
        var metadata = new StructuredMetadata { Provider = "Mistral", Operation = bind is null ? "Text" : "Structured", SchemaName = schemaName, CorrelationId = request.CorrelationId };
        var timeout = request.TotalTimeout ?? _totalTimeout;
        StructuredResult<T> Failure(StructuredErrorKind kind, HttpStatusCode? status = null) => StructuredResult<T>.Failure(new()
        {
            Kind = kind,
            Message = "Mistral execution failed.",
            HttpStatusCode = status,
            IsTransient = kind is StructuredErrorKind.RateLimited or StructuredErrorKind.ProviderUnavailable or StructuredErrorKind.TransportFailure or StructuredErrorKind.DeadlineExceeded,
            TotalTimeout = kind == StructuredErrorKind.DeadlineExceeded ? timeout : null,
        }, metadata);

        if(caller.IsCancellationRequested)
            throw new StructuredOperationCanceledException(metadata, [], caller);

        if(!ValidTimeout(timeout))
            return Failure(StructuredErrorKind.InvalidRequest);

        using var budget = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, budget.Token);
        var token = linked.Token;
        StructuredResult<T> result;
        try
        {
            BoundOutput<T>? output;
            Dictionary<string, object?> payload;
            try
            {
                if(request.GetType() != typeof(StructuredRequest) || request.Stream || request.InactivityTimeout is not null ||
                    request.IncludeReasoningSummary || request.Progress is not null || request.MaxOutputTokens is <= 0 ||
                    bind is null && (request.Vocabularies is not null || request.OutputSpecification is not null))
                    throw new ArgumentException("Unsupported request controls.");

                output = bind?.Invoke();
                var model = SelectModel(request.ModelSelection ?? taskModel ?? _defaultModel);
                metadata = metadata with { RequestedModel = model.ModelId };
                var prompt = request.Instructions ?? instructions;
                if(prompt is not null && String.IsNullOrWhiteSpace(prompt) || bind is not null && String.IsNullOrWhiteSpace(prompt))
                    throw new ArgumentException("Invalid instructions.");

                var messages = Messages(request);
                if(prompt is not null || output?.Guidance is not null)
                    messages.Insert(0, new { role = "system", content = String.Join("\n\n", new[] { prompt, output?.Guidance }.Where(x => x is not null)) });

                payload = new() { ["model"] = model.ModelId, ["messages"] = messages, ["stream"] = false };
                if(request.MaxOutputTokens is { } max)
                    payload["max_tokens"] = max;

                if(output is not null)
                    payload["response_format"] = new { type = "json_schema", json_schema = new { name = output.SchemaName, description = output.Task.Description, schema = output.CreateSchema(), strict = true } };
            }
            catch(StructuredSchemaException)
            {
                return Failure(StructuredErrorKind.UnsupportedSchema);
            }
            catch(ArgumentException)
            {
                return Failure(StructuredErrorKind.InvalidRequest);
            }

            string? key;
            try
            {
                var resolver = request.CredentialResolver ?? taskCredentials ?? _credentials;
                key = resolver is null ? null : await resolver(token).AsTask().WaitAsync(token).ConfigureAwait(false);
                if(String.IsNullOrWhiteSpace(key))
                    return Failure(StructuredErrorKind.CredentialsMissing);

                if(key.Any(Char.IsWhiteSpace) || key.Any(Char.IsControl))
                    return Failure(StructuredErrorKind.Authentication);
            }
            catch(Exception exception) when(exception is not OperationCanceledException and not OutOfMemoryException)
            {
                return Failure(StructuredErrorKind.Authentication);
            }

            using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(_baseAddress, "chat/completions"));
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            message.Content = JsonContent.Create(payload);
            using var response = await _http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            metadata = metadata with { ProviderRequestId = response.Headers.TryGetValues("x-request-id", out var ids) ? ids.FirstOrDefault() : null };
            if(!response.IsSuccessStatusCode)
                return Failure(response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized => StructuredErrorKind.Authentication,
                    HttpStatusCode.Forbidden => StructuredErrorKind.PermissionDenied,
                    HttpStatusCode.TooManyRequests => StructuredErrorKind.RateLimited,
                    _ => (int)response.StatusCode >= 500 ? StructuredErrorKind.ProviderUnavailable : StructuredErrorKind.ProviderRejected,
                }, response.StatusCode);

            await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            var bytes = new byte[8192];
            int count;
            while((count = await stream.ReadAsync(bytes, token).ConfigureAwait(false)) != 0)
            {
                if(buffer.Length + count > _maxResponseBytes)
                    return Failure(StructuredErrorKind.InvalidResponse);

                buffer.Write(bytes, 0, count);
            }

            using var document = JsonDocument.Parse(buffer.ToArray());
            var root = document.RootElement;
            metadata = metadata with { ResponseId = StringValue(root, "id"), ResolvedModel = StringValue(root, "model"), Usage = ReadUsage(root) };
            var choices = root.GetProperty("choices");
            if(choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() != 1)
                result = Failure(StructuredErrorKind.InvalidResponse);
            else
            {
                var choice = choices[0];
                var finish = StringValue(choice, "finish_reason");
                var text = StringValue(choice.GetProperty("message"), "content");
                result = finish == "length" ? Failure(StructuredErrorKind.IncompleteOutput)
                    : finish != "stop" || text is null ? Failure(StructuredErrorKind.InvalidResponse)
                    : output is not null ? output.ReadOutput(text, metadata)
                    : StructuredResult<T>.Success((T)(object)text, metadata);
            }

            token.ThrowIfCancellationRequested();
            var observer = request.UsageObserver ?? _usageObserver;
            if(observer is not null && metadata.Usage is { } usage)
            {
                try
                {
                    await observer(new()
                    {
                        Provider = "Mistral",
                        RequestedModel = metadata.RequestedModel!,
                        Usage = usage,
                        Metadata = metadata,
                        Succeeded = result.IsSuccess,
                        FailureKind = result.Error?.Kind,
                    }, token).AsTask().WaitAsync(token).ConfigureAwait(false);
                }
                catch(Exception exception) when(!token.IsCancellationRequested && exception is not OutOfMemoryException) { }
            }

            token.ThrowIfCancellationRequested();
            return result;
        }
        catch(OperationCanceledException)
        {
            if(caller.IsCancellationRequested)
                throw new StructuredOperationCanceledException(metadata, [], caller);

            return Failure(budget.IsCancellationRequested ? StructuredErrorKind.DeadlineExceeded : StructuredErrorKind.TransportFailure);
        }
        catch(HttpRequestException)
        {
            return Failure(StructuredErrorKind.TransportFailure);
        }
        catch(IOException)
        {
            return Failure(StructuredErrorKind.TransportFailure);
        }
        catch(Exception exception) when(exception is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or ArgumentException or OverflowException)
        {
            return Failure(StructuredErrorKind.InvalidResponse);
        }
    }

    static bool ValidTimeout(TimeSpan timeout) => timeout > TimeSpan.Zero && timeout <= TimeSpan.FromHours(24);

    static void ValidateModel(ModelSelection model)
    {
        model.Validate();
        if(model.ReasoningEffort is not null)
            throw new ArgumentException("Reasoning effort is unsupported.");
    }

    ModelSelection SelectModel(ModelSelection selection)
    {
        ValidateModel(selection);
        if(selection.ProfileName is not { } profile)
            return selection;

        return _profiles.TryGetValue(profile, out var model) ? model : throw new ArgumentException("Unknown model profile.");
    }

    static List<object> Messages(StructuredRequest request)
    {
        if((request.Input is null) == (request.Messages is null))
            throw new ArgumentException("Select input or messages.");

        if(request.Input is { } input)
            return !String.IsNullOrWhiteSpace(input) ? [new { role = "user", content = input }] : throw new ArgumentException("Input must be nonblank.");

        var messages = new List<object>();
        foreach(var message in request.Messages!)
        {
            if(message is null || message.Content is null || message.Content.Count == 0 ||
                message.Role is not (MessageRole.System or MessageRole.User or MessageRole.Assistant))
                throw new ArgumentException("Unsupported message.");

            var parts = new List<string>();
            foreach(var part in message.Content)
            {
                if(part is not TextPart text || String.IsNullOrWhiteSpace(text.Text))
                    throw new ArgumentException("Only nonblank text parts are supported.");

                parts.Add(text.Text);
            }

            messages.Add(new { role = message.Role.ToString().ToLowerInvariant(), content = String.Join("\n", parts) });
        }

        return messages.Count > 0 ? messages : throw new ArgumentException("Messages must not be empty.");
    }

    static string? StringValue(JsonElement root, string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    static StructuredUsage? ReadUsage(JsonElement root)
    {
        if(!root.TryGetProperty("usage", out var usage) || usage.ValueKind == JsonValueKind.Null)
            return null;

        return new() { InputTokens = Count(usage, "prompt_tokens"), OutputTokens = Count(usage, "completion_tokens"), TotalTokens = Count(usage, "total_tokens") };
    }

    static long? Count(JsonElement usage, string name) => usage.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null
        ? value.GetInt64() : null;
}
