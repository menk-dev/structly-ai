namespace Structly.AI;

/// <summary>Supported conversation message roles.</summary>
public enum MessageRole
{
    /// <summary>System instructions.</summary>
    System,
    /// <summary>Developer instructions.</summary>
    Developer,
    /// <summary>User input.</summary>
    User,
    /// <summary>Earlier assistant output.</summary>
    Assistant
}

/// <summary>Ordered message content, snapshotted before credential resolution.</summary>
public sealed record StructuredMessage(MessageRole Role, IReadOnlyList<ContentPart> Content);

/// <summary>A supported input part.</summary>
public abstract record ContentPart
{
    /// <summary>Gets whether to mark an explicit provider cache boundary after this part.</summary>
    public bool CacheBreakpoint { get; init; }
}

/// <summary>A nonblank text part.</summary>
public sealed record TextPart(string Text) : ContentPart;

/// <summary>An image URL or base64 data URL. The library never downloads it.</summary>
public sealed record ImagePart : ContentPart
{
    /// <summary>Gets an absolute HTTPS or PNG/JPEG/WebP/GIF base64 data URL.</summary>
    public required string Url { get; init; }
    /// <summary>Gets the requested image detail.</summary>
    public ImageDetail Detail { get; init; } = ImageDetail.Auto;
}

/// <summary>Provider image detail.</summary>
public enum ImageDetail
{
    /// <summary>Provider decides.</summary>
    Auto,
    /// <summary>Low detail.</summary>
    Low,
    /// <summary>High detail.</summary>
    High
}

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

/// <summary>Free text generation, with the same response execution controls.</summary>
public sealed record TextRequest
{
    /// <summary>Gets response input and execution controls. Vocabularies and output specifications are unsupported.</summary>
    public required StructuredRequest Request { get; init; }
    /// <summary>Gets optional nonblank instructions, resent on continuation.</summary>
    public string? Instructions { get; init; }
}
