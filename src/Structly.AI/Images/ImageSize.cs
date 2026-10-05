namespace Structly.AI.Imaging;

/// <summary>Image size presets; actual model support is provider policy.</summary>
public enum ImageSize
{
    /// <summary>Provider chooses.</summary>
    Auto,
    /// <summary>1024 by 1024.</summary>
    Square,
    /// <summary>1024 by 1536.</summary>
    Portrait,
    /// <summary>1536 by 1024.</summary>
    Landscape,
    /// <summary>1536 by 864.</summary>
    Wide,
}
