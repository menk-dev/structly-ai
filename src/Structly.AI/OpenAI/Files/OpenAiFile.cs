namespace Structly.AI.OpenAI;

/// <summary>Batch file metadata returned by the provider.</summary>
public sealed record OpenAiFile
{
    /// <summary>Gets the provider resource identifier.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the provider filename.</summary>
    public required string Filename { get; init; }
    /// <summary>Gets the provider-reported file size in bytes.</summary>
    public required long Bytes { get; init; }
    /// <summary>Gets the provider file purpose.</summary>
    public string? Purpose { get; init; }
}
