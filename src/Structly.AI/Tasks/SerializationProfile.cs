namespace Structly.AI;

/// <summary>Immutable supported serialization settings; arbitrary converters are excluded.</summary>
public sealed record SerializationProfile
{
    /// <summary>Gets the naming policy. Explicit JsonPropertyName takes precedence.</summary>
    public PropertyNaming Naming { get; init; } = PropertyNaming.CamelCase;
}
