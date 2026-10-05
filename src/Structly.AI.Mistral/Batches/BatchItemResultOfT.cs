namespace Structly.AI.Mistral;

/// <summary>A batch item identity and its output or failure.</summary>
public sealed class BatchItemResult<T>
{
    /// <summary>Creates an item with a nonblank custom ID.</summary>
    public BatchItemResult(string customId, StructuredResult<T> result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customId);
        ArgumentNullException.ThrowIfNull(result);
        CustomId = customId;
        Result = result;
    }

    /// <summary>Gets the application custom ID.</summary>
    public string CustomId { get; }
    /// <summary>Gets the output or failure with retained metadata.</summary>
    public StructuredResult<T> Result { get; }
}
