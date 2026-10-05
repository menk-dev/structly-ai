namespace Structly.AI.Mistral;

/// <summary>Ordered batch items and reported usage grouped by model.</summary>
public sealed class BatchImportResult<T>
{
    /// <summary>Copies items and summaries into immutable collections.</summary>
    public BatchImportResult(IEnumerable<BatchItemResult<T>> items, IEnumerable<BatchUsageSummary> usageByModel)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(usageByModel);
        Items = Array.AsReadOnly(items.ToArray());
        UsageByModel = Array.AsReadOnly(usageByModel.ToArray());
    }

    /// <summary>Gets items in manifest order, including missing-item failures.</summary>
    public IReadOnlyList<BatchItemResult<T>> Items { get; }
    /// <summary>Gets summaries in order of first model occurrence.</summary>
    public IReadOnlyList<BatchUsageSummary> UsageByModel { get; }
}
