namespace Structly.AI;

/// <summary>Classifies local, transport, provider and output failures.</summary>
public enum StructuredErrorKind
{
    /// <summary>Invalid per-call options.</summary>
    InvalidRequest,
    /// <summary>Unsupported schema or vocabulary.</summary>
    UnsupportedSchema,
    /// <summary>Malformed or rejected credentials, or a failed resolver.</summary>
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
    InvalidOutput,
    /// <summary>No selected credentials are available.</summary>
    CredentialsMissing,
    /// <summary>A provider batch item failed without an HTTP classification.</summary>
    BatchItemFailed,
}
