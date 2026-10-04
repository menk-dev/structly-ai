using System.Net;
using System.Text.Json;

namespace Structly.AI;

/// <summary>A path-specific diagnostic using serialized names and [] for collection items.</summary>
public sealed record StructuredIssue(string Path, string Code, string Message);

/// <summary>A static schema or vocabulary cannot satisfy the output contract.</summary>
public sealed class StructuredSchemaException : ArgumentException
{
    /// <summary>Creates an exception from schema diagnostics.</summary>
    public StructuredSchemaException(IEnumerable<StructuredIssue> issues)
        : base("The structured output contract is invalid. Inspect Issues for paths and corrections.")
    {
        ArgumentNullException.ThrowIfNull(issues);
        Issues = Array.AsReadOnly(issues.ToArray());
        if (Issues.Count == 0)
            throw new ArgumentException("At least one issue is required.", nameof(issues));
    }

    /// <summary>Gets immutable actionable diagnostics.</summary>
    public IReadOnlyList<StructuredIssue> Issues { get; }
}

/// <summary>Classifies local, transport, provider and output failures.</summary>
public enum StructuredErrorKind
{
    /// <summary>Invalid per-call options.</summary>
    InvalidRequest,
    /// <summary>Unsupported schema or vocabulary.</summary>
    UnsupportedSchema,
    /// <summary>Missing or rejected credentials.</summary>
    Authentication,
    /// <summary>Access denied.</summary>
    PermissionDenied,
    /// <summary>Provider rate or quota limit.</summary>
    RateLimited,
    /// <summary>Provider unavailable.</summary>
    ProviderUnavailable,
    /// <summary>Provider rejected the request.</summary>
    ProviderRejected,
    /// <summary>Transport failed.</summary>
    TransportFailure,
    /// <summary>Total execution budget expired.</summary>
    DeadlineExceeded,
    /// <summary>Streaming activity deadline expired.</summary>
    InactivityExceeded,
    /// <summary>Provider refused output.</summary>
    Refused,
    /// <summary>Provider output was incomplete.</summary>
    IncompleteOutput,
    /// <summary>Malformed provider envelope.</summary>
    InvalidResponse,
    /// <summary>JSON does not satisfy the typed contract.</summary>
    InvalidOutput
}

/// <summary>A categorized failure without sensitive provider content.</summary>
public sealed record StructuredError
{
    readonly IReadOnlyList<StructuredIssue> _issues = Array.AsReadOnly(Array.Empty<StructuredIssue>());
    readonly StructuredErrorKind _kind;
    readonly string _message = "";
    readonly TimeSpan? _retryAfter;
    /// <summary>Gets the failure category.</summary>
    public required StructuredErrorKind Kind
    {
        get => _kind;
        init => _kind = Enum.IsDefined(value) ? value : throw new ArgumentException("Unknown error category.", nameof(value));
    }
    /// <summary>Gets a safe diagnostic message.</summary>
    public required string Message
    {
        get => _message;
        init => _message = !String.IsNullOrWhiteSpace(value) ? value : throw new ArgumentException("A safe nonblank message is required.", nameof(value));
    }
    /// <summary>Gets whether a host-owned retry may be useful.</summary>
    public bool IsTransient { get; init; }
    /// <summary>Gets the optional HTTP status.</summary>
    public HttpStatusCode? HttpStatusCode { get; init; }
    /// <summary>Gets the optional provider retry delay.</summary>
    public TimeSpan? RetryAfter
    {
        get => _retryAfter;
        init => _retryAfter = value < TimeSpan.Zero ? throw new ArgumentOutOfRangeException(nameof(value)) : value;
    }
    /// <summary>Gets or initializes immutable diagnostics.</summary>
    public IReadOnlyList<StructuredIssue> Issues
    {
        get => _issues;
        init => _issues = Array.AsReadOnly((value ?? throw new ArgumentNullException(nameof(value))).ToArray());
    }
}

/// <summary>A safe nonfatal execution diagnostic.</summary>
public sealed record StructuredWarning(string Code, string Message);

/// <summary>Token counts reported by a provider. Missing counts remain null.</summary>
public sealed record StructuredUsage
{
    readonly long? _inputTokens;
    readonly long? _outputTokens;
    readonly long? _totalTokens;
    readonly long? _cachedInputTokens;
    readonly long? _cacheWriteTokens;
    readonly long? _reasoningTokens;
    readonly long? _inputTextTokens;
    readonly long? _inputImageTokens;
    readonly long? _outputImageTokens;
    /// <summary>Gets reported input tokens.</summary>
    public long? InputTokens
    {
        get => _inputTokens;
        init => _inputTokens = value < 0 ? throw new ArgumentOutOfRangeException(nameof(value), "Usage counts must be nonnegative or null.") : value;
    }
    /// <summary>Gets reported output tokens.</summary>
    public long? OutputTokens
    {
        get => _outputTokens;
        init => _outputTokens = value < 0 ? throw new ArgumentOutOfRangeException(nameof(value), "Usage counts must be nonnegative or null.") : value;
    }
    /// <summary>Gets reported total tokens.</summary>
    public long? TotalTokens
    {
        get => _totalTokens;
        init => _totalTokens = value < 0 ? throw new ArgumentOutOfRangeException(nameof(value), "Usage counts must be nonnegative or null.") : value;
    }
    /// <summary>Gets reported cached input tokens.</summary>
    public long? CachedInputTokens
    {
        get => _cachedInputTokens;
        init => _cachedInputTokens = value < 0 ? throw new ArgumentOutOfRangeException(nameof(value), "Usage counts must be nonnegative or null.") : value;
    }
    /// <summary>Gets reported cache write tokens.</summary>
    public long? CacheWriteTokens
    {
        get => _cacheWriteTokens;
        init => _cacheWriteTokens = value < 0 ? throw new ArgumentOutOfRangeException(nameof(value), "Usage counts must be nonnegative or null.") : value;
    }
    /// <summary>Gets reported reasoning tokens.</summary>
    public long? ReasoningTokens
    {
        get => _reasoningTokens;
        init => _reasoningTokens = value < 0 ? throw new ArgumentOutOfRangeException(nameof(value), "Usage counts must be nonnegative or null.") : value;
    }
    /// <summary>Gets reported image input text tokens.</summary>
    public long? InputTextTokens
    {
        get => _inputTextTokens;
        init => _inputTextTokens = value < 0 ? throw new ArgumentOutOfRangeException(nameof(value), "Usage counts must be nonnegative or null.") : value;
    }
    /// <summary>Gets reported image input image tokens.</summary>
    public long? InputImageTokens
    {
        get => _inputImageTokens;
        init => _inputImageTokens = value < 0 ? throw new ArgumentOutOfRangeException(nameof(value), "Usage counts must be nonnegative or null.") : value;
    }
    /// <summary>Gets reported image output image tokens.</summary>
    public long? OutputImageTokens
    {
        get => _outputImageTokens;
        init => _outputImageTokens = value < 0 ? throw new ArgumentOutOfRangeException(nameof(value), "Usage counts must be nonnegative or null.") : value;
    }
}

/// <summary>Identity and accounting retained even when output processing fails.</summary>
public sealed record StructuredMetadata
{
    readonly JsonElement? _rawResponse;
    /// <summary>Gets a local execution identifier.</summary>
    public Guid ExecutionId { get; init; } = Guid.NewGuid();
    /// <summary>Gets the operation name.</summary>
    public string? Operation { get; init; }
    /// <summary>Gets the caller correlation identifier.</summary>
    public string? CorrelationId { get; init; }
    /// <summary>Gets the provider name.</summary>
    public string? Provider { get; init; }
    /// <summary>Gets the requested model.</summary>
    public string? RequestedModel { get; init; }
    /// <summary>Gets the resolved model.</summary>
    public string? ResolvedModel { get; init; }
    /// <summary>Gets the provider response identifier.</summary>
    public string? ResponseId { get; init; }
    /// <summary>Gets the provider request identifier.</summary>
    public string? ProviderRequestId { get; init; }
    /// <summary>Gets reported usage without inferred counts.</summary>
    public StructuredUsage? Usage { get; init; }
    /// <summary>Gets output text only when explicitly captured.</summary>
    public string? OutputText { get; init; }
    /// <summary>Gets a detached raw envelope only when explicitly captured.</summary>
    public JsonElement? RawResponse
    {
        get => _rawResponse;
        init => _rawResponse = value?.Clone();
    }
}

/// <summary>A typed success or categorized failure with retained metadata.</summary>
public sealed class StructuredResult<T>
{
    StructuredResult(bool success, T? value, StructuredError? error, StructuredMetadata metadata,
        IEnumerable<StructuredWarning>? warnings)
    {
        IsSuccess = success;
        Value = value;
        Error = error;
        Metadata = metadata;
        Warnings = Array.AsReadOnly(warnings?.ToArray() ?? []);
    }

    /// <summary>Gets success independently of the value's default value.</summary>
    public bool IsSuccess { get; }
    /// <summary>Gets the successful value, or default on failure.</summary>
    public T? Value { get; }
    /// <summary>Gets the failure, or null on success.</summary>
    public StructuredError? Error { get; }
    /// <summary>Gets retained execution metadata.</summary>
    public StructuredMetadata Metadata { get; }
    /// <summary>Gets immutable nonfatal diagnostics.</summary>
    public IReadOnlyList<StructuredWarning> Warnings { get; }

    /// <summary>Creates a successful result.</summary>
    public static StructuredResult<T> Success(T value, StructuredMetadata metadata,
        IEnumerable<StructuredWarning>? warnings = null)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(metadata);
        return new(true, value, null, metadata, warnings);
    }

    /// <summary>Creates a failed result without a usable value.</summary>
    public static StructuredResult<T> Failure(StructuredError error, StructuredMetadata metadata,
        IEnumerable<StructuredWarning>? warnings = null)
    {
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(metadata);
        return new(false, default, error, metadata, warnings);
    }

    /// <summary>Returns the successful value or throws with the retained failure metadata.</summary>
    public T EnsureSuccess() => IsSuccess ? Value! : throw new StructuredOperationException(Error!, Metadata, Warnings);
}

/// <summary>An explicitly requested exception view of a categorized failure.</summary>
public sealed class StructuredOperationException : Exception
{
    /// <summary>Creates an exception retaining the original error and accounting metadata.</summary>
    public StructuredOperationException(StructuredError error, StructuredMetadata metadata,
        IReadOnlyList<StructuredWarning> warnings) : base((error ?? throw new ArgumentNullException(nameof(error))).Message)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(warnings);
        Error = error;
        Metadata = metadata;
        Warnings = Array.AsReadOnly(warnings.ToArray());
    }
    /// <summary>Gets the failure.</summary>
    public StructuredError Error { get; }
    /// <summary>Gets execution metadata.</summary>
    public StructuredMetadata Metadata { get; }
    /// <summary>Gets immutable warnings.</summary>
    public IReadOnlyList<StructuredWarning> Warnings { get; }
}
