# Execution, streaming and callbacks

You can reuse `OpenAiClient` and `StructuredTask<T>` across concurrent calls. Before reading
credentials, each call copies vocabularies, messages, embedding inputs, provider metadata
and model options. Credentials, clocks, warnings and callbacks are kept separate for each call.

The library does not manage or change your `HttpClient`. Set its timeout to
`Timeout.InfiniteTimeSpan`. Disable redirects and automatic HTTP retries if you need to
keep credentials on the selected endpoint and send exactly one HTTP attempt.

## Timeouts and cancellation

`OpenAiClientOptions.TotalTimeout` defaults to 120 seconds. A request's `TotalTimeout`
overrides it. Text and prewarm requests put these settings in `Request`; embedding and
image requests expose `TotalTimeout` directly.

The total timeout must be positive and no more than 24 hours. An inactivity timeout must
be positive. Total time includes validation, credential resolution, HTTP sending and reading,
output processing and waiting for callbacks. The configured `TimeProvider` supplies elapsed
time and UTC time for retry dates.

Synchronous application code cannot be interrupted. This includes output constructors and
setters, and callback code before its first `await`. Keep it short. The library checks
cancellation between execution steps and returns a timeout error if time has expired,
even if output processing returned a value.

Before the result is finalized, outcomes have this order of precedence:

1. Caller cancellation.
2. Total timeout.
3. Streaming inactivity timeout.
4. The provider or output result.

Caller cancellation throws `StructuredOperationCanceledException` with the original token,
metadata and read-only warnings. An already-cancelled call sends nothing. Cancelling after
completion does not change the result. Library timeouts return transient `DeadlineExceeded`
or `InactivityExceeded` errors. Independent transport cancellation, including
`HttpClient.Timeout`, returns transient `TransportFailure`.

If an output constructor or setter throws a cancellation exception, the result is
nontransient `InvalidOutput`, unless caller cancellation or a library timeout takes precedence.

When asynchronous resolvers, sends, reads or callbacks ignore cancellation, the library
stops waiting at the timeout. It observes later faults and disposes responses and streams
that arrive late. Requests, responses and body resources are disposed on every exit.
The provider may continue processing and charging after the library stops waiting.

## Streaming and progress

Set `Stream=true` to stream a response. A progress callback is optional.
`InactivityTimeout` starts when SSE body reading begins and resets for each complete
SSE line, including comments and keep-alives. Partial bytes do not reset it.

A client inactivity timeout applies only to streaming calls. Setting request inactivity
or `Progress` on a nonstreaming call returns `InvalidRequest` before credentials or HTTP.
While a delta progress callback runs, the inactivity timer pauses. It restarts with the
full interval afterward. Callback time still counts toward the total timeout and the
one-second callback limit.

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

`DisplayProgressAsync` and `RecordUsageAsync` are application callbacks. Progress arrives
in order: `Started`, then `OutputTextDelta` or `ReasoningSummaryDelta`, then `Completed`
after output validation. `IncludeReasoningSummary` requests `reasoning.summary=auto` and
requires streaming. Check that the model supports it. `OutputIndex` and `SummaryIndex`
identify summary parts. Raw reasoning events are ignored. Progress text and summaries may
contain sensitive data and have not passed final output validation.

The SSE reader supports fragmented UTF-8, multiline data, CR, LF and CRLF line endings,
an initial byte-order mark, and comments. It ignores unknown and unrelated event types.
Malformed JSON, invalid recognized delta data, mismatched event types and invalid UTF-8
return `InvalidResponse`.

A final completed, incomplete or failed response event is required. End-of-stream, a
standalone error event or deltas alone cannot produce success. The final output passes
the same refusal, status and schema checks as a buffered response.

`MaxResponseBytes` defaults to 16 MiB. It limits the buffered response or all bytes read
from SSE, including comments. Embeddings use the same limit. Images use
`MaxImageResponseBytes`, which defaults to 128 MiB to allow for base64 data. Reading stops
and the stream is disposed after the final response event; the connection need not close
first. Raw capture stores the final response object, not the SSE event history.

### Progress callback failures

The library waits at most one second, or the remaining total timeout, for a progress
callback. A fault or cancellation adds `ProgressObserverFailed`; a timeout adds
`ProgressObserverTimedOut`. Both disable later progress callbacks, while stream reading
and usage recording continue. If no time remains, the warning is `ProgressObserverSkipped`.
Callback tokens are separate from the caller token. The total timeout still takes precedence.

## Usage callbacks

When any valid usage count is available, one `StructuredUsageEvent` goes to the request
observer, or the client observer if the request has none. Refusal, incomplete output and
invalid output can still include usage. No usage means no callback. Counts are independently
nullable. Malformed counts add `InvalidUsage` warnings; they are not replaced with zeros
or calculated costs. Cancellation before a final streaming response may leave charges unknown.

The event describes the result before the callback runs. `CallerCancelled` records
cancellation at that point. Later cancellation or timeouts may change the final result.

The callback token is separate from caller cancellation. The library waits at most five
seconds, or the remaining total timeout. A fault or cancellation adds `UsageObserverFailed`;
a timeout adds `UsageObserverTimedOut`; no time remaining adds `UsageObserverSkipped`.
Callback failure does not replace the operation result, but total timeout and caller
cancellation still take precedence. The callback is not retried. Code that ignores its
token may continue after the operation returns; later faults are observed.

Cache diagnostics retain comparison results, reasons and counts, including unknown strings.
`Usage.CachedInputTokens` is the provider's reported cached input count. Embedding
`prompt_tokens` becomes `InputTokens`. Images retain reported text and image token counts.
Missing counts stay unknown. See [advanced features](advanced.md).

Read final usage from `result.Metadata` or cancellation-exception metadata. If you record
both callback data and result data, deduplicate using `ExecutionId`. Malformed responses,
abandoned sends and early cancellation can leave usage unknown. The library cannot report
every charge the provider makes.

## Diagnostics and captured data

The library does not log bodies, prompts, credentials, output or exceptions automatically.
Errors and warnings use fixed descriptions rather than provider or application exception
text. Response IDs, model IDs, correlation IDs and token counts remain available.

`CaptureRawResponse` and `CaptureOutputText` retain potentially sensitive data, including
on failed operations that may be billed. Your application must decide what to store and log.

## Retries

The library sends one attempt and does not wait or retry automatically. Your application
sets job time limits, concurrency limits, retry delays and schedules. Avoid retry loops
in both an HTTP handler and application code if you need a known maximum number of attempts.

`RetryAfter` preserves valid `Retry-After` seconds or dates. Past dates become zero using
the configured `TimeProvider`. `IsTransient` helps with retry decisions; it does not promise
that another attempt is safe or free. Quota exhaustion is not transient. Refused, incomplete
and invalid output can be billed.

`IdempotencyKey` passes through as an OpenAI header. The library does not store previous
requests or guarantee that the provider prevents duplicate processing. `CorrelationId`
stays local; `OpenAiResponseOptions.Metadata` is sent to the provider. A timeout or network
failure can happen after the provider accepts the request. Consider that before retrying.

## Task execution defaults

Set `StructuredTaskOptions.ExecutionDefaults` when creating or registering a task to
avoid repeating execution settings in every request:

```csharp
var task = StructuredTask.Create<Ticket>(new()
{
    Instructions = "Extract the reported support issue.",
    ExecutionDefaults = new()
    {
        TotalTimeout = TimeSpan.FromSeconds(30),
        InactivityTimeout = TimeSpan.FromSeconds(5),
        MaxOutputTokens = 800
    }
});
var result = await client.ExecuteAsync(task, text, cancellationToken);
```

Each setting uses the per-call value first, then the task default, then the client default
where available. Clients have no default output-token cap. Null means inherit; to change
a task cap for one call, supply another positive cap. Invalid explicit overrides fail
validation rather than falling back. Requests are not modified.

Total timeouts must be positive and at most 24 hours. Inactivity timeouts and token caps
must be positive. Defaults are validated during task creation and apply to hosted calls,
direct task calls and bound-output calls, including the string overloads. A task's inactivity
default applies only when the call streams; an explicit inactivity setting on a non-streaming
request remains invalid.

Conversations capture the original task defaults at creation. Output and configuration
branches preserve those defaults, and each turn can override them. Responses batches inherit
only the output-token cap; configure batch transport deadlines with `BatchOperationOptions`.
Prewarming does not inherit these generation settings.
