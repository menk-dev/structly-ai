# Offline testing

Install `Structly.AI.Testing --version 0.4.0` alongside the matching core package.
It targets .NET 10 and has no test-framework dependency.

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
will validate the output and retain usage on failure. Builders never inspect the request schema.

For explicit fixture filling, call `ResponseEnvelopes.CompleteExample(task, partialJson, vocabularies)`.
It adds missing object properties from `CreateExample`, including nested objects. It preserves
supplied values, nulls, arrays and unknown properties, then validates and throws `ArgumentException`
if the completed output is invalid. Filling is never implicit. Supplied arrays are not expanded.
See the runnable [consumer](../examples/Structly.AI.Consumer/Program.cs).
