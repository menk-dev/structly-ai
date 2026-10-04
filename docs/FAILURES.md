# Failure handling

Use `IsSuccess`, including for value-type outputs; a nullable `Value` is not a success
indicator. Success has a usable `Value` and no error. Failure has one `StructuredError`
with safe `Message`, `Kind`, `IsTransient`, optional HTTP status/RetryAfter and local
`Issues`. Inspect `Metadata` and `Warnings` on both paths.

```csharp
var result = await client.ExecuteAsync(task, request, cancellationToken);
if (result.IsSuccess)
    Save(result.Value!);
else if (result.Error!.Kind == StructuredErrorKind.RateLimited)
    ScheduleRetry(result.Error.RetryAfter);
else
    RecordFailure(result.Error.Kind, result.Error.Issues, result.Metadata);
```

These host functions are illustrative. The library does not schedule or send retries.

| Kind | Handling |
| --- | --- |
| InvalidRequest | Correct input/options; request was rejected locally. |
| UnsupportedSchema | Correct the DTO or supplied vocabularies using issue paths/codes. |
| Authentication / PermissionDenied | Fix the selected credential or access permissions; no credential fallback. |
| RateLimited | Respect available RetryAfter and host/job budget before another attempt. |
| ProviderUnavailable / TransportFailure | Inspect IsTransient and safe metadata; decide retry policy in the host. |
| ProviderRejected | Correct the request for the selected provider/model. |
| Refused | Treat as a provider refusal; no usable typed output. |
| IncompleteOutput | Output did not complete, even if some text was parseable. |
| InvalidResponse | Malformed envelope or feature-specific provider output. |
| InvalidOutput | JSON failed schema or DTO deserialization. |
| DeadlineExceeded | Total operation budget expired. |
| InactivityExceeded | Streaming read stalled beyond the idle deadline. |

`IsTransient` is classification, not an instruction or retry guarantee. Available usage
can accompany refusal, incomplete or invalid output. Provider response/request IDs,
requested/resolved model and correlation help investigate failures. Raw bodies are
excluded from safe errors; explicit capture options can contain sensitive data.

Null method arguments throw `ArgumentNullException`; invalid client/task settings throw
`ArgumentException` (including `StructuredSchemaException`). Invalid per-call values
produce categorized results before auth/HTTP. `EnsureSuccess()` returns the value or
throws `StructuredOperationException`, retaining Error, Metadata and Warnings.

Caller cancellation throws `StructuredOperationCanceledException` with the original
token, Metadata and Warnings. Catch it as `OperationCanceledException` for standard host
cancellation behavior or as the specialized type to account for billed work. Caller
cancellation takes precedence over deadlines. Callback failures add warnings and do
not replace the main outcome. See [execution](EXECUTION.md) and [usage](USAGE.md).
