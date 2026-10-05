namespace Structly.AI;

/// <summary>An image URL or base64 data URL. The library never downloads it.</summary>
public sealed record ImagePart : ContentPart
{
    /// <summary>Gets an absolute HTTPS or PNG/JPEG/WebP/GIF base64 data URL.</summary>
    public required string Url { get; init; }
    /// <summary>Gets the requested image detail.</summary>
    public ImageDetail Detail { get; init; } = ImageDetail.Auto;
}
