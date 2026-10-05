using System.Net;

namespace Structly.AI;

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
    /// <summary>Gets the numeric HTTP status.</summary>
    public int? HttpStatusCodeValue => HttpStatusCode is { } status ? (int)status : null;
    /// <summary>Gets the effective total timeout on deadline failures.</summary>
    public TimeSpan? TotalTimeout { get; init; }
    /// <summary>Gets the effective inactivity timeout on inactivity failures.</summary>
    public TimeSpan? InactivityTimeout { get; init; }
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
