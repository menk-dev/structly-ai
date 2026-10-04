# Usage accounting

`result.Metadata.Usage` contains provider-reported nullable nonnegative counts. Missing
counts remain unknown; the library never invents zero, totals, pricing or cost. Metadata
also carries ExecutionId, CorrelationId, operation, provider, requested/resolved model,
response/request IDs and optional cache diagnostics. Unknown resolved identity stays unknown.

Accounting runs before typed/vector/image processing. Refusal, incomplete responses,
malformed output and other billed failures can retain usage. If caller cancellation
occurs after usage capture, inspect `StructuredOperationCanceledException.Metadata`.
An abandoned request may still be billed without usable usage metadata.

```csharp
var request = new StructuredRequest
{
    Input = input,
    CorrelationId = jobId,
    UsageObserver = (item, token) => ledger.RecordAsync(
        item.Metadata.ExecutionId, item.Metadata.Usage,
        item.Succeeded, item.FailureKind, item.CallerCancelled, token)
};
```

Here `ledger.RecordAsync` is a host function returning ValueTask. A request observer
overrides the optional client observer. There is one observer delivery per execution
when usage is available; absence of usage does not create a fabricated accounting event.
If recording both observer and result metadata, deduplicate by ExecutionId. Delivery is
best effort, bounded by five seconds or the remaining total budget.
It is not a transactional exactly-once ledger. Callback exceptions/timeouts produce
safe warnings without masking success, failure or cancellation. Callbacks should honor
their token.

Common counts include InputTokens, OutputTokens, TotalTokens, CachedInputTokens,
CacheWriteTokens and ReasoningTokens. Embedding prompt_tokens maps to input tokens;
image usage preserves available text/image breakdowns. Provider omission means unknown.
Cache diagnostics preserve unknown diagnostic strings and valid independent counts;
malformed counts add warnings. Neither a cache key nor prewarming guarantees reuse or cost.

Default diagnostics exclude prompts, output, raw reasoning, credentials and raw provider
error bodies. There is no default logging sink. If you enable `CaptureOutputText` or
`CaptureRawResponse`, choose your own retention and access policy. Streaming raw capture
contains the terminal response envelope, not an SSE transcript. Progress may include
output text and explicitly requested reasoning summaries; see [execution](EXECUTION.md).
