namespace Structly.AI;

/// <summary>Groups failures for application exception mapping, independently of retryability.</summary>
public enum StructuredErrorCategory
{
    /// <summary>The configured provider cannot serve the operation.</summary>
    Unavailable,
    /// <summary>The request was invalid, denied or refused.</summary>
    Rejected,
    /// <summary>The provider did not return usable output.</summary>
    InvalidOutput,
}
