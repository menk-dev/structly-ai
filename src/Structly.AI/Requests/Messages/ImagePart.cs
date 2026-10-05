namespace Structly.AI;

/// <summary>An image URL or base64 data URL. The library never downloads it.</summary>
public sealed record ImagePart : ContentPart
{
    /// <summary>Creates a data URL from PNG, JPEG, WebP or GIF signatures. Signature detection does not decode or validate the entire image.</summary>
    public static ImagePart FromBytes(ReadOnlySpan<byte> bytes, string? contentType = null, ImageDetail detail = ImageDetail.Auto)
    {
        if(!Enum.IsDefined(detail))
            throw new ArgumentOutOfRangeException(nameof(detail));

        var detected = bytes.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ? "image/png" :
            bytes.StartsWith(new byte[] { 255, 216, 255 }) ? "image/jpeg" :
            bytes.StartsWith("GIF87a"u8) || bytes.StartsWith("GIF89a"u8) ? "image/gif" :
            bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WEBP"u8) ? "image/webp" :
            throw new ArgumentException("Expected PNG, JPEG, WebP or GIF image bytes.", nameof(bytes));
        if(contentType is not null && !String.Equals(contentType.Trim(), detected, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Content type must match the detected image format.", nameof(contentType));

        return new() { Url = $"data:{detected};base64,{Convert.ToBase64String(bytes)}", Detail = detail };
    }

    /// <summary>Gets an absolute HTTPS or PNG/JPEG/WebP/GIF base64 data URL.</summary>
    public required string Url { get; init; }
    /// <summary>Gets the requested image detail.</summary>
    public ImageDetail Detail { get; init; } = ImageDetail.Auto;
}
