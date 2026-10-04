namespace Structly.AI.Imaging;

/// <summary>Positive custom pixel dimensions, overriding a preset.</summary>
public sealed record ImageDimensions(int Width, int Height);

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
    Wide
}

/// <summary>Image generation quality.</summary>
public enum ImageQuality
{
    /// <summary>Provider chooses.</summary>
    Auto,
    /// <summary>Low quality.</summary>
    Low,
    /// <summary>Medium quality.</summary>
    Medium,
    /// <summary>High quality.</summary>
    High
}

/// <summary>Image background policy.</summary>
public enum ImageBackground
{
    /// <summary>Provider chooses.</summary>
    Auto,
    /// <summary>Opaque.</summary>
    Opaque,
    /// <summary>Transparent, requiring PNG or WebP.</summary>
    Transparent
}

/// <summary>Generated image file format.</summary>
public enum ImageFormat
{
    /// <summary>PNG.</summary>
    Png,
    /// <summary>JPEG.</summary>
    Jpeg,
    /// <summary>WebP.</summary>
    WebP
}

/// <summary>Image API generation; editing, URL fetching and streaming are unsupported.</summary>
public sealed record ImageGenerationRequest
{
    /// <summary>Gets the nonblank image prompt.</summary>
    public required string Prompt { get; init; }
    /// <summary>Gets explicit model/profile selection; reasoning is unsupported.</summary>
    public required ModelSelection ModelSelection { get; init; }
    /// <summary>Gets image count (1–10).</summary>
    public int Count { get; init; } = 1;
    /// <summary>Gets the size preset.</summary>
    public ImageSize Size { get; init; } = ImageSize.Auto;
    /// <summary>Gets positive dimensions overriding Size. Model-specific limits are checked by the provider.</summary>
    public ImageDimensions? CustomDimensions { get; init; }
    /// <summary>Gets image quality.</summary>
    public ImageQuality Quality { get; init; } = ImageQuality.Auto;
    /// <summary>Gets background policy.</summary>
    public ImageBackground Background { get; init; } = ImageBackground.Auto;
    /// <summary>Gets output format.</summary>
    public ImageFormat Format { get; init; } = ImageFormat.Png;
    /// <summary>Gets an optional total execution budget.</summary>
    public TimeSpan? TotalTimeout { get; init; }
    /// <summary>Gets request credentials.</summary>
    public Func<CancellationToken, ValueTask<string?>>? CredentialResolver { get; init; }
    /// <summary>Gets an observer overriding the client observer.</summary>
    public Func<StructuredUsageEvent, CancellationToken, ValueTask>? UsageObserver { get; init; }
    /// <summary>Gets a local correlation identifier.</summary>
    public string? CorrelationId { get; init; }
    /// <summary>Gets idempotency header passthrough without a deduplication guarantee.</summary>
    public string? IdempotencyKey { get; init; }
    /// <summary>Gets whether to retain the sensitive provider envelope.</summary>
    public bool CaptureRawResponse { get; init; }
}

/// <summary>Validated base64 image data and its file media type.</summary>
public sealed record GeneratedImage(string Base64Data, string MediaType)
{
    /// <summary>Returns newly decoded image bytes.</summary>
    public byte[] ToBytes() => Convert.FromBase64String(Base64Data);
}
