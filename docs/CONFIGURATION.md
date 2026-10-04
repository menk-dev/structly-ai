# Configuration

Structly.AI requires .NET 10. Task, request and result types are in `Structly.AI`.
The OpenAI client is in `Structly.AI.OpenAI`. Embedding and image types are in
`Structly.AI.Embeddings` and `Structly.AI.Imaging`. The core package has no NuGet dependencies.

For ASP.NET Core and Generic Host, the optional [hosting package](HOSTING.md) registers
the client and reads settings from configuration.

## HTTP client

Create an `OpenAiClient` with an `HttpClient` that your application manages. Keep both
for reuse across calls. Set `HttpClient.Timeout` to `Timeout.InfiniteTimeSpan` so
Structly.AI's timeouts control execution.

Structly.AI does not change the HTTP client's headers, base address or timeout, and
does not dispose it. Your application configures handlers, connection pooling and proxies,
then disposes the HTTP client when it is no longer needed. Structly.AI copies client
options and task settings when they are created. Clients and tasks support concurrent calls.

```csharp
using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
var client = new OpenAiClient(http, new OpenAiClientOptions
{
    DefaultModel = new() { ProfileName = "extract" },
    Profiles = new Dictionary<string, ModelSelection>
    {
        ["extract"] = new() { ModelId = configuredModel }
    },
    CredentialResolver = Credentials.FromEnvironment("OPENAI_API_KEY"),
    TotalTimeout = TimeSpan.FromSeconds(120)
});
```

## Models

In this example, `configuredModel` comes from your application's settings. Select exactly
one `ModelId` or `ProfileName`. A request's selection overrides the task's selection;
the task's selection overrides the client default.

Profiles may set `ReasoningEffort.Low`, `Medium` or `High`. The library does not keep a
model list, rank models or choose a fallback. Structured output, free text and prewarming
use `Profiles`. Embeddings and images need their own model selection and use
`EmbeddingProfiles` and `ImageProfiles`. They reject reasoning settings.
See [advanced examples](ADVANCED.md).

## Credentials

The library uses the request's credential resolver if set, otherwise the task's resolver,
otherwise the client's resolver. If that resolver returns no key, execution returns
`CredentialsMissing`; the library does not try another resolver. Caller cancellation still throws.

`Credentials.FromStatic` stores the key you supply. `Credentials.FromEnvironment` reads
the named environment variable on each call. A custom asynchronous resolver can choose
a key for each tenant. The library does not read credentials unless you configure a resolver.
Set request credentials rather than changing shared HTTP authorization headers.

## Defaults and limits

`BaseAddress` defaults to `https://api.openai.com/v1/`. A custom endpoint must implement
the same API. By default, operations have a 120-second total timeout, no streaming
inactivity timeout, buffered responses, `Store=false`, and no capture of raw responses
or output text.

Responses and embeddings are limited to 16 MiB. Images are limited to 128 MiB.
Change `MaxResponseBytes` and `MaxImageResponseBytes` if needed.

Supply either `StructuredRequest.Input` or ordered `Messages`, not both. The library
copies request data before reading credentials. Do not change collections while that
copy is being made. Callbacks, output constructors and setters must return promptly;
cancellation cannot interrupt synchronous application code. See [execution](EXECUTION.md)
for timeout and callback rules and [usage](USAGE.md) for token usage callbacks.
