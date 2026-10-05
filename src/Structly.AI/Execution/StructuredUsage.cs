namespace Structly.AI;

/// <summary>Token counts reported by a provider. Missing counts remain null.</summary>
public sealed record StructuredUsage
{
    readonly long? _inputTokens;
    readonly long? _outputTokens;
    readonly long? _totalTokens;
    readonly long? _cachedInputTokens;
    readonly long? _cacheWriteTokens;
    readonly long? _reasoningTokens;
    readonly long? _inputTextTokens;
    readonly long? _inputImageTokens;
    readonly long? _outputImageTokens;
    readonly long? _outputTextTokens;
    /// <summary>Gets reported input tokens.</summary>
    public long? InputTokens
    {
        get => _inputTokens;
        init => _inputTokens = value < 0 ? throw new ArgumentOutOfRangeException(nameof(value), "Usage counts must be nonnegative or null.") : value;
    }
    /// <summary>Gets reported output tokens.</summary>
    public long? OutputTokens
    {
        get => _outputTokens;
        init => _outputTokens = value < 0 ? throw new ArgumentOutOfRangeException(nameof(value), "Usage counts must be nonnegative or null.") : value;
    }
    /// <summary>Gets reported total tokens.</summary>
    public long? TotalTokens
    {
        get => _totalTokens;
        init => _totalTokens = value < 0 ? throw new ArgumentOutOfRangeException(nameof(value), "Usage counts must be nonnegative or null.") : value;
    }
    /// <summary>Gets reported cached input tokens.</summary>
    public long? CachedInputTokens
    {
        get => _cachedInputTokens;
        init => _cachedInputTokens = value < 0 ? throw new ArgumentOutOfRangeException(nameof(value), "Usage counts must be nonnegative or null.") : value;
    }
    /// <summary>Gets reported cache write tokens.</summary>
    public long? CacheWriteTokens
    {
        get => _cacheWriteTokens;
        init => _cacheWriteTokens = value < 0 ? throw new ArgumentOutOfRangeException(nameof(value), "Usage counts must be nonnegative or null.") : value;
    }
    /// <summary>Gets reported reasoning tokens.</summary>
    public long? ReasoningTokens
    {
        get => _reasoningTokens;
        init => _reasoningTokens = value < 0 ? throw new ArgumentOutOfRangeException(nameof(value), "Usage counts must be nonnegative or null.") : value;
    }
    /// <summary>Gets reported image input text tokens.</summary>
    public long? InputTextTokens
    {
        get => _inputTextTokens;
        init => _inputTextTokens = value < 0 ? throw new ArgumentOutOfRangeException(nameof(value), "Usage counts must be nonnegative or null.") : value;
    }
    /// <summary>Gets reported image input image tokens.</summary>
    public long? InputImageTokens
    {
        get => _inputImageTokens;
        init => _inputImageTokens = value < 0 ? throw new ArgumentOutOfRangeException(nameof(value), "Usage counts must be nonnegative or null.") : value;
    }
    /// <summary>Gets reported image output image tokens.</summary>
    public long? OutputImageTokens
    {
        get => _outputImageTokens;
        init => _outputImageTokens = value < 0 ? throw new ArgumentOutOfRangeException(nameof(value), "Usage counts must be nonnegative or null.") : value;
    }
    /// <summary>Gets reported image output text tokens.</summary>
    public long? OutputTextTokens
    {
        get => _outputTextTokens;
        init => _outputTextTokens = value < 0 ? throw new ArgumentOutOfRangeException(nameof(value), "Usage counts must be nonnegative or null.") : value;
    }
}
