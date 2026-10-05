# Failure handling

Check `IsSuccess` to determine whether an operation succeeded. Checking `Value` for null
does not work for every output type. A successful result has a usable `Value` and no error.
A failed result has one `StructuredError` with `Message`, `Kind`, `IsTransient`, optional
HTTP status and `RetryAfter`, and local validation `Issues`. Check `Metadata` and `Warnings`
for either outcome.

```csharp
var result = await client.ExecuteAsync(task, request, cancellationToken);
if (result.IsSuccess)
    Save(result.Value!);
else if (result.Error!.Kind == StructuredErrorKind.RateLimited)
    ScheduleRetry(result.Error.RetryAfter);
else
    RecordFailure(result.Error.Kind, result.Error.Issues, result.Metadata);
```

`Save`, `ScheduleRetry` and `RecordFailure` are example application functions. The library
does not schedule or send retries.

| Kind | What to do |
| --- | --- |
| InvalidRequest | Correct the input or options. The library rejected the request before sending it. |
| UnsupportedSchema | Correct the output type or vocabularies using the issue paths and codes. |
| CredentialsMissing | Supply a resolver that returns a nonblank key. An explicitly selected resolver does not fall back. |
| BatchItemFailed | Inspect the application batch record; the provider reported an item error without an HTTP status. |
| Authentication / PermissionDenied | Check credentials and access permissions. The library does not try another key. |
| RateLimited | Check `RetryAfter` and your application's time limit before retrying. |
| ProviderUnavailable / TransportFailure | Check `IsTransient` and metadata, then decide whether to retry. |
| ProviderRejected | Correct the request for the chosen provider and model. |
| Refused | The provider refused the request. There is no usable typed output. |
| IncompleteOutput | The provider did not finish the output, even if some text can be parsed. |
| InvalidResponse | The provider returned a malformed response or invalid data for the operation. |
| InvalidOutput | The JSON failed schema validation or could not be deserialized. |
| DeadlineExceeded | The total timeout expired. |
| InactivityExceeded | The stream did not provide a complete SSE line within the inactivity timeout. |

`IsTransient` helps you decide whether to retry. It does not mean a retry will succeed
or avoid another charge. Refused, incomplete and invalid output can include usage counts.
Response and request IDs, model IDs and correlation IDs can help investigate failures.
Error messages omit raw provider bodies. Explicit capture options may retain sensitive data.

## Results and exceptions

Null method arguments throw `ArgumentNullException`. Invalid client or task settings
throw `ArgumentException`, including `StructuredSchemaException`. Invalid per-call values
return an error result before credentials are read or HTTP requests are sent.

`EnsureSuccess()` returns the value on success. On failure, it throws
`StructuredOperationException` with `Error`, `Metadata` and `Warnings`.

Caller cancellation throws `StructuredOperationCanceledException` with the original token,
metadata and warnings. Catch it as `OperationCanceledException` for normal cancellation
handling, or use the specific type to read any available token counts. Caller cancellation
takes precedence over timeouts. Callback failures add warnings without replacing the
operation's outcome. See [execution](execution.md) and [usage](usage.md).

`HttpStatusCodeValue` exposes the numeric status alongside `HttpStatusCode`.
Deadline failures include the effective `TotalTimeout`; inactivity failures include the effective
`InactivityTimeout`. Their messages identify the expired budget. Invalid-output messages include
at most five escaped path/code pairs, capped at 512 characters, with an omitted-issue indicator.
They exclude issue messages and output values. `Issues` retains every diagnostic.
