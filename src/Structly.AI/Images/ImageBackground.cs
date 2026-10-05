namespace Structly.AI.Imaging;

/// <summary>Image background policy.</summary>
public enum ImageBackground
{
    /// <summary>Provider chooses.</summary>
    Auto,
    /// <summary>Opaque.</summary>
    Opaque,
    /// <summary>Transparent, requiring PNG or WebP.</summary>
    Transparent,
}
