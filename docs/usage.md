# Token counts and usage callbacks

`result.Metadata.Usage` contains the token counts reported by the provider. Each count
is nullable and nonnegative. A missing count means unknown, not zero. The library does
not calculate missing totals, prices or costs.

Metadata also includes `ExecutionId`, `CorrelationId`, the operation and provider,
requested and resolved model IDs, response and request IDs, and optional cache diagnostics.
If the provider does not report the resolved model or response ID, that value stays unknown.

The library reads usage before processing typed output, vectors or images. Counts can
therefore be available even when the provider refuses a request or returns incomplete
or invalid output. After caller cancellation, check
`StructuredOperationCanceledException.Metadata`. A request that you stopped waiting for
may still be billed even if no usage was received.

## Record usage

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

Here, `ledger.RecordAsync` is application code returning `ValueTask`. A request's
`UsageObserver` replaces the client observer. When usage is available, the library calls
one observer once per execution. When usage is missing, it does not call the observer.
If you record both callback data and result metadata, deduplicate using `ExecutionId`.

The library waits at most five seconds, or the remaining total timeout, for the callback.
It does not retry the callback or guarantee that your record was saved. Exceptions and
timeouts add warnings without replacing the operation's result or cancellation. Callbacks
should respect their cancellation token.

## Available counts

Counts include `InputTokens`, `OutputTokens`, `TotalTokens`, `CachedInputTokens`,
`CacheWriteTokens` and `ReasoningTokens`. Embedding `prompt_tokens` becomes `InputTokens`.
Image usage retains any reported text and image breakdowns.

Cache diagnostics keep unknown diagnostic strings and valid counts. Malformed counts
add warnings. A cache key or prewarm request does not guarantee reuse or lower costs.

## Captured data

Default diagnostics omit prompts, output, raw reasoning, credentials and raw provider
error bodies. The library does not log automatically. If you enable `CaptureOutputText`
or `CaptureRawResponse`, decide where that data is stored and who can access it.

For streaming, raw capture contains the final response object, not every SSE event.
Progress callbacks may receive output text and requested reasoning summaries.
See [execution](execution.md).

Usage events expose required `Provider`, `RequestedModel`, and `Usage` properties directly.
`ResolvedModel` may be absent; use `RequestedModel` as the accounting fallback. Counts are
nullable `long`: null means unknown, not zero. When an application needs `int`, use a checked
conversion, for example `int? count = usage.InputTokens is { } n ? checked((int)n) : null;`.
There is no universal maximum promised by this package.

Batch imports notify once for each item with reported usage on each import, including failed
output validation. Re-importing can repeat notifications. Persist ledger entries with a unique
execution-ID constraint and record usage transactionally. Do not add aggregate batch counts to
item counts or infer usage for missing results. See [batches](batches.md).

Successful batch imports also expose per-model reported sums and per-field coverage through
`BatchImportResult<T>.UsageByModel`. See [batch summaries](batches.md#usage-summaries).
For DI dependencies, Hosting provides [scoped usage observers](hosting.md#di-usage-observers).
