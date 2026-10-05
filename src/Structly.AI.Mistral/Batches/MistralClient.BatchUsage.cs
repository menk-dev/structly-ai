namespace Structly.AI.Mistral;

public sealed partial class MistralClient
{
    static IReadOnlyList<BatchUsageSummary> SummarizeBatchUsage<T>(IReadOnlyList<BatchItemResult<T>> items, List<StructuredWarning> warnings)
    {
        var summaries = new List<BatchUsageSummary>();
        foreach(var group in items.GroupBy(item => item.Result.Metadata.ResolvedModel ?? item.Result.Metadata.RequestedModel!, StringComparer.Ordinal))
        {
            var usage = group.Select(item => item.Result.Metadata.Usage).OfType<StructuredUsage>().ToArray();
            var counts = new int[10];
            long? Sum(Func<StructuredUsage, long?> select, int index, string field)
            {
                long sum = 0;
                bool overflow = false;
                foreach(var item in usage)
                {
                    if(select(item) is not { } count)
                        continue;

                    counts[index]++;
                    if(overflow)
                        continue;

                    try
                    {
                        sum = checked(sum + count);
                    }
                    catch(OverflowException)
                    {
                        overflow = true;
                        warnings.Add(new("BatchUsageOverflow", $"Reported usage for model '{group.Key}', field '{field}' exceeds Int64.MaxValue."));
                    }
                }

                return overflow || counts[index] == 0 ? null : sum;
            }

            var reported = new StructuredUsage
            {
                InputTokens = Sum(item => item.InputTokens, 0, nameof(StructuredUsage.InputTokens)),
                OutputTokens = Sum(item => item.OutputTokens, 1, nameof(StructuredUsage.OutputTokens)),
                TotalTokens = Sum(item => item.TotalTokens, 2, nameof(StructuredUsage.TotalTokens)),
                CachedInputTokens = Sum(item => item.CachedInputTokens, 3, nameof(StructuredUsage.CachedInputTokens)),
                CacheWriteTokens = Sum(item => item.CacheWriteTokens, 4, nameof(StructuredUsage.CacheWriteTokens)),
                ReasoningTokens = Sum(item => item.ReasoningTokens, 5, nameof(StructuredUsage.ReasoningTokens)),
                InputTextTokens = Sum(item => item.InputTextTokens, 6, nameof(StructuredUsage.InputTextTokens)),
                InputImageTokens = Sum(item => item.InputImageTokens, 7, nameof(StructuredUsage.InputImageTokens)),
                OutputImageTokens = Sum(item => item.OutputImageTokens, 8, nameof(StructuredUsage.OutputImageTokens)),
                OutputTextTokens = Sum(item => item.OutputTextTokens, 9, nameof(StructuredUsage.OutputTextTokens)),
            };
            summaries.Add(new()
            {
                Model = group.Key,
                ItemCount = group.Count(),
                ItemsWithUsage = usage.Length,
                ReportedUsage = reported,
                Coverage = new()
                {
                    InputTokens = counts[0],
                    OutputTokens = counts[1],
                    TotalTokens = counts[2],
                    CachedInputTokens = counts[3],
                    CacheWriteTokens = counts[4],
                    ReasoningTokens = counts[5],
                    InputTextTokens = counts[6],
                    InputImageTokens = counts[7],
                    OutputImageTokens = counts[8],
                    OutputTextTokens = counts[9],
                },
            });
        }

        return summaries.AsReadOnly();
    }
}
