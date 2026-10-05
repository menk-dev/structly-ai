# Offline testing

Install `Structly.AI.Testing --version 0.7.0` alongside the matching core package.
It targets .NET 10 and has no test-framework dependency.

Use `OpenAiTestFixture` to run the real client with queued offline responses:

```csharp
using var fixture = new OpenAiTestFixture();
var task = StructuredTask.Create<Answer>("Extract the answer.");
fixture.Enqueue(ResponseEnvelopes.CompletedText("{\"value\":\"hello\"}"));
var result = await fixture.Client.ExecuteAsync(task, "input", cancellationToken);
var answer = result.EnsureSuccess();
var requestJson = fixture.Requests[0].ReadJson();
```

The fixture configures a test model and static fake credentials. Its handler never connects
to a provider. `Enqueue` copies the envelope and accepts an optional HTTP status; responses
are consumed in request arrival order. Local validation and pre-cancelled calls do not
consume responses. Invalid output is processed by the real client without fixture filling.
An unexpected request throws `InvalidOperationException` when the queue is empty.
`PendingResponseCount` exposes unused responses for assertions.

`Requests` returns immutable snapshots of method, URI and body, including unexpected
requests. Headers, including authorization, are excluded. `ReadJson()` returns detached
JSON. Concurrent requests are supported, but response assignment follows arrival order.
Dispose the fixture after all operations finish; previously captured requests remain readable.

`ResponseEnvelopes.CompletedText` preserves supplied text exactly, including invalid JSON.
`CompletedJson`, `Refusal`, `Incomplete`, and `Embeddings` return detached JSON envelopes.
Set model, response ID (Responses), and usage explicitly when your test needs accounting.
`ToHttpResponse` creates a fresh disposable HTTP response for each call.

```csharp
using Structly.AI.Testing;

sealed class FakeHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(ResponseEnvelopes.ToHttpResponse(
            ResponseEnvelopes.CompletedText("{broken", usage: new() { TotalTokens = 7 })));
}
```

Use a normal `OpenAiClient` with this handler and a static fake credential. The real client
will validate the output and retain usage on failure. Envelope builders preserve supplied output.

For explicit fixture filling, call `ResponseEnvelopes.CompleteExample(task, partialJson, vocabularies)`.
It adds missing object properties from `CreateExample`, including nested objects. It preserves
supplied values, nulls, arrays and unknown properties, then validates and throws `ArgumentException`
if the completed output is invalid. Filling is never implicit. Supplied arrays are not expanded.
See the runnable [consumer](../examples/Structly.AI.Consumer/Program.cs).

## Responding from the request schema

When payload types are private or vocabularies change per request, complete output from
`text.format.schema` in the actual Responses request:

```csharp
using var fixture = new OpenAiTestFixture(null, (request, token) =>
{
    var output = ResponseEnvelopes.CompleteFromRequest(request.ReadJson(), "{}");
    return ValueTask.FromResult(ResponseEnvelopes.ToHttpResponse(
        ResponseEnvelopes.CompletedJson(output)));
});
```

`CompleteFromRequest` accepts request JSON as a string or `JsonElement`, and partial output
as a JSON string. It returns detached output JSON, not a provider envelope. It fills missing
properties, including objects inside supplied arrays, without replacing supplied values or
explicit nulls. Missing arrays use their minimum item count; supplied arrays are not expanded.
Enums use the first transmitted value, so per-request vocabularies are respected.

Completion supports Structly-emitted schemas. It checks transmitted types, enum values,
bounds, patterns and formats. Missing patterned strings require explicit values. Unsupported
schema keywords or constraints that cannot be satisfied throw `ArgumentException`. Generation
is bounded to 10,000 visited nodes, 1,000 generated items per array, 100,000 generated characters
per string and 1 MiB of serialized output. CLR-only rules, such as numeric representability and
set equality, are still checked by the real client when reading the response.

The responder receives a detached request and cancellation token and must return a fresh HTTP
response for each call. It runs outside the capture lock and can execute concurrently. Queued
responses take precedence; the responder handles requests when the queue is empty. Without a
responder, exhaustion still throws. The fixture captures requests before invoking the responder.

Pass `OpenAiClientOptions` as the first constructor argument to configure credentials, model,
total and inactivity timeouts, or a controlled `TimeProvider`. Supplied options replace the
fixture defaults; they must include a default model and fake credentials unless the test is
checking missing credentials. Options are snapshotted at construction. The fixture transport
never connects to the provider, and its HTTP timeout remains infinite so Structly's execution
budgets control cancellation.
