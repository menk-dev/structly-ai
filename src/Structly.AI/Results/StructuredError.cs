using System.Net;

namespace Structly.AI;

/// <summary>A categorized failure without sensitive provider content.</summary>
public sealed record StructuredError
{
    /// <summary>Gets the application failure category. This does not indicate retryability.</summary>
    public StructuredErrorCategory Category => Kind switch
    {
        StructuredErrorKind.CredentialsMissing or StructuredErrorKind.Authentication or
        StructuredErrorKind.RateLimited or StructuredErrorKind.ProviderUnavailable or
        StructuredErrorKind.TransportFailure or StructuredErrorKind.DeadlineExceeded or
        StructuredErrorKind.InactivityExceeded => StructuredErrorCategory.Unavailable,
        StructuredErrorKind.IncompleteOutput or StructuredErrorKind.InvalidResponse or
        StructuredErrorKind.InvalidOutput or StructuredErrorKind.BatchItemFailed => StructuredErrorCategory.InvalidOutput,
        _ => StructuredErrorCategory.Rejected,
    };

    /// <summary>Gets a single-line summary from library-controlled wording and available status and timeout fields.</summary>
    public string Summary => (Kind switch
    {
        StructuredErrorKind.InvalidRequest => "The request is invalid.",
        StructuredErrorKind.UnsupportedSchema => "The output schema is unsupported.",
        StructuredErrorKind.Authentication => "Provider authentication failed.",
        StructuredErrorKind.PermissionDenied => "Provider access was denied.",
        StructuredErrorKind.RateLimited => "The provider rate or quota limit was reached.",
        StructuredErrorKind.ProviderUnavailable => "The provider is unavailable.",
        StructuredErrorKind.ProviderRejected => "The provider rejected the request.",
        StructuredErrorKind.TransportFailure => "The provider connection failed.",
        StructuredErrorKind.DeadlineExceeded => "The total timeout expired.",
        StructuredErrorKind.InactivityExceeded => "The inactivity timeout expired.",
        StructuredErrorKind.Refused => "The provider refused the requested output.",
        StructuredErrorKind.IncompleteOutput => "The provider output is incomplete.",
        StructuredErrorKind.InvalidResponse => "The provider response is invalid.",
        StructuredErrorKind.InvalidOutput => "The output does not satisfy the contract.",
        StructuredErrorKind.CredentialsMissing => "Provider credentials are missing.",
        StructuredErrorKind.BatchItemFailed => "The provider batch item failed.",
        _ => "The structured operation failed.",
    }) +
        (HttpStatusCodeValue is { } status ? $" HTTP {status}." : "") +
        (TotalTimeout is { } total ? $" Total timeout: {total.ToString("c", System.Globalization.CultureInfo.InvariantCulture)}." : "") +
        (InactivityTimeout is { } idle ? $" Inactivity timeout: {idle.ToString("c", System.Globalization.CultureInfo.InvariantCulture)}." : "");

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
