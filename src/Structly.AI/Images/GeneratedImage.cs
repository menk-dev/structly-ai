namespace Structly.AI.Imaging;

/// <summary>Validated base64 image data and its file media type.</summary>
public sealed record GeneratedImage(string Base64Data, string MediaType)
{
    /// <summary>Returns newly decoded image bytes.</summary>
    public byte[] ToBytes() => Convert.FromBase64String(Base64Data);
}
