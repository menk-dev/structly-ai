# Execution, streaming and accounting

Reuse OpenAiClient and StructuredTask<T> concurrently. Each call snapshots vocabularies, messages, embedding inputs,
provider metadata and model options before resolving credentials. Request credentials,
clocks, warnings and callbacks belong to that call. The library neither owns nor mutates
HttpClient. Configure its Timeout to Timeout.InfiniteTimeSpan and disable redirects and
transport retries when credential isolation and exactly one transport attempt are needed.

## Deadlines and cancellation

OpenAiClientOptions.TotalTimeout defaults to 120 seconds; StructuredRequest.TotalTimeout
replaces it. Text and prewarm wrap those controls in Request; embeddings and images
expose TotalTimeout directly. All operations share the same budget and observer policy.
Total budgets must be positive and at most 24 hours; inactivity budgets must be positive. Total time includes local
validation, credential resolution, sending, body reading, output processing and callback
waiting. The configurable TimeProvider supplies monotonic elapsed time and UTC retry dates.
Synchronous host code, including DTO constructors/setters and delegate invocation before
its first await, cannot be preempted: it must return promptly. Cancellation is checked at
execution boundaries; an expired budget overrides a returned output.

Caller cancellation throws StructuredOperationCanceledException with the original caller
token, Metadata and immutable Warnings. Before finalization, caller cancellation wins over
total expiration, total wins over inactivity, and inactivity wins over provider/output
outcomes. Pre-cancelled calls send nothing. Cancellation after completion is not retroactive.
Library deadlines return transient DeadlineExceeded or InactivityExceeded errors. Independent
transport cancellation, including HttpClient.Timeout, returns transient TransportFailure.

For noncooperative async resolvers, HTTP sends, content acquisition, reads or callbacks,
the client stops waiting at the deadline, observes eventual faults and disposes late
responses/streams. It disposes its request, response and body resources on every exit.
Abandoning a wait does not guarantee the provider stopped processing or charging the call.

## Streaming and progress

Set Stream=true explicitly; streaming does not require a callback. Optional request or
client InactivityTimeout starts when SSE body reading begins and resets on each complete
SSE line, including comments and keep-alives. Partial bytes do not extend it. A client
default is applied only to streaming calls; specifying request inactivity or Progress on
a nonstreaming call returns InvalidRequest before credentials or HTTP.

```csharp
var request = new StructuredRequest
{
    Input = "Extract the ticket from this message.",
    Stream = true,
    TotalTimeout = TimeSpan.FromSeconds(90),
    InactivityTimeout = TimeSpan.FromSeconds(15),
    IncludeReasoningSummary = true,
    Progress = (item, token) => DisplayProgressAsync(item, token),
    UsageObserver = (item, token) => RecordUsageAsync(item, token)
};
var result = await client.ExecuteAsync(task, request, cancellationToken);
```

DisplayProgressAsync and RecordUsageAsync are host callbacks. Progress is ordered:
Started, OutputTextDelta/ReasoningSummaryDelta, then Completed after typed output
validation. IncludeReasoningSummary requests reasoning.summary=auto and is valid only
with streaming; model support remains the caller's responsibility. OutputIndex and
SummaryIndex identify summary parts. Raw reasoning events are ignored. Progress fragments
and summaries are sensitive and are not validated final output.

SSE supports UTF-8 fragments, multiline data, CR/LF/CRLF, an initial BOM and comments.
Unknown and ancillary event types are ignored; malformed JSON/recognized delta shapes,
event/type mismatch and invalid UTF-8 return InvalidResponse. A completed/incomplete/failed
terminal envelope is mandatory; EOF, a standalone error event or deltas alone cannot
produce success. Terminal output passes the same refusal/status/schema rules as a buffered
response. MaxResponseBytes (16 MiB default) bounds the complete buffered response or bytes
consumed from SSE, including comments. Images use the independent MaxImageResponseBytes (128 MiB default)
to accommodate base64; embeddings use MaxResponseBytes. A terminal event ends reading and disposes the stream;
there is no requirement to wait for connection EOF. Raw capture retains the terminal
response envelope, not the stream's event history.

Progress callbacks wait at most one second or the remaining total budget. A fault,
cancellation or timeout adds ProgressObserverFailed/ProgressObserverTimedOut and disables
subsequent progress callbacks; stream processing and usage observation continue. No-budget
callbacks receive ProgressObserverSkipped. Callback tokens are bounded independently of
the caller token; total expiration still takes precedence.

## Usage and safe diagnostics

When any valid usage count is available, the request UsageObserver replaces the client
observer and receives one StructuredUsageEvent. Refusal, incomplete output and invalid
output still retain usage. Missing usage produces no event. Counts remain independently
nullable; malformed counts add InvalidUsage warnings and do not invent zeroes or costs.
Streaming cancellation before a terminal envelope may leave billing unknown.

The event describes the output outcome before observation; CallerCancelled records caller
cancellation at that point. Later cancellation/deadlines can change the final outcome.
The observer token is independent of caller cancellation and bounded by five seconds or
the remaining total budget. Fault/cancellation adds UsageObserverFailed, observer timeout
adds UsageObserverTimedOut, and no remaining budget adds UsageObserverSkipped. Observer
failure preserves the primary outcome; total expiration or caller cancellation still wins.
Delivery is best effort and is never retried. Noncooperative callbacks can continue after
return and must tolerate cancellation; their late faults are observed.

Cache diagnostics retain comparison outcome/reason strings and counts, including unknown
strings. Diagnostics explain a comparison, while Usage.CachedInputTokens measures reported
reuse. Embeddings map prompt_tokens to InputTokens. Images retain reported text/image
input and output token details; no missing count is inferred. See [Advanced features](ADVANCED.md).

Read result.Metadata or cancellation-exception Metadata for final available accounting.
Deduplicate event/result records using ExecutionId if consuming both. Truncated/malformed
responses, abandoned sends and preterminal cancellation can leave usage unknown: the
library cannot promise observation of every billed call.

There is no automatic body, prompt, credential, output or exception logging. Error messages
and warnings use safe fixed descriptions, never provider/host exception text. Response IDs,
model IDs, correlation IDs and independent usage counts remain available for diagnostics.
CaptureRawResponse and CaptureOutputText explicitly retain sensitive data, including on
billed failures. Host callbacks and any host logging must choose their own data policy.

## Retry ownership and duplicate work

The library performs one attempt and never sleeps/retries automatically. Hosts own job
budgets, concurrency limits, backoff and retry scheduling. Avoid retry loops in both a
handler and the host when a bounded number of attempts matters. RetryAfter retains valid
Retry-After seconds/date hints, with past dates clamped to zero against the configured
TimeProvider. IsTransient is a scheduling hint, not a promise that replay is safe or free;
quota exhaustion is not transient. Refusal, incomplete or invalid output can be billed.

IdempotencyKey is only an optional OpenAI header passthrough. The library provides no
replay store, local deduplication or provider deduplication guarantee. CorrelationId stays
local; OpenAiResponseOptions.Metadata is explicit provider-visible metadata. A timeout
or network failure can occur after the provider accepted the request. Account for that
uncertainty before scheduling a new attempt.
