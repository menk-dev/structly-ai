namespace Structly.AI.Mistral;

/// <summary>Provider file metadata.</summary>
public sealed record MistralFile
{
    /// <summary>Gets the resource identifier.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the filename.</summary>
    public required string Filename { get; init; }
    /// <summary>Gets the reported size, which can be absent.</summary>
    public long? Bytes { get; init; }
    /// <summary>Gets the file purpose.</summary>
    public string? Purpose { get; init; }
}
