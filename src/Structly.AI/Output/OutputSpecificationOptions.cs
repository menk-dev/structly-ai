namespace Structly.AI;

/// <summary>Controls guidance generated from the resolved output contract.</summary>
public sealed record OutputSpecificationOptions
{
    /// <summary>Gets whether to include serialized fields and their complete schema constraints.</summary>
    public bool IncludeFields { get; init; } = true;
    /// <summary>Gets whether to include a locally validated example.</summary>
    public bool IncludeExample { get; init; } = true;
    /// <summary>Gets optional nonblank additional instructions.</summary>
    public string? AdditionalInstructions { get; init; }
    /// <summary>Gets an optional contract-valid JSON example, required for constraints without a reliable generated sample.</summary>
    public string? ExampleJson { get; init; }
}
