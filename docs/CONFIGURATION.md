# Configuration

Structly.AI targets .NET 10. Use `Structly.AI` for task/request/result contracts and
`Structly.AI.OpenAI` for the provider. Embeddings and images use `Structly.AI.Embeddings`
and `Structly.AI.Imaging`. The runtime uses framework APIs only.

Create a long-lived `OpenAiClient` with a caller-owned `HttpClient`. Set HTTP timeout to
`Timeout.InfiniteTimeSpan` so the library owns deadlines. The client never mutates the
transport's headers, base address or timeout and does not dispose it. Supply handlers,
pooling, proxy and connection policy in the host. Dispose the transport when its owner
shuts down. Client options and task settings are snapshotted; reuse clients/tasks concurrently.

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

`configuredModel` is a host configuration value. Select exactly one `ModelId` or
`ProfileName`. Request selection replaces task selection, which replaces the client's
default. Profiles can specify `ReasoningEffort.Low`, `Medium` or `High`. The library
has no model list, ranking or fallback. Response/text/prewarm use `Profiles`; auxiliary
calls require their own model selection and use `EmbeddingProfiles` or `ImageProfiles`.
Those operations reject reasoning settings. See [advanced examples](ADVANCED.md).

Credential precedence is request > task > client. The selected resolver never falls
back if it returns a missing key. `Credentials.FromStatic` captures the explicitly
supplied credential; `Credentials.FromEnvironment` rereads the named variable each call.
A custom async resolver can select tenant credentials. No ambient default credential is
read. Set request credentials instead of changing shared HTTP authorization headers.
Missing credentials return `Authentication`; caller cancellation still throws.

`BaseAddress` defaults to `https://api.openai.com/v1/`; custom endpoints must speak the
same API. Defaults are a 120-second total budget, no inactivity deadline, buffered
responses, `Store=false`, and no raw output/envelope capture. Response and embedding
envelopes are limited to 16 MiB; images to 128 MiB. Configure `MaxResponseBytes` and
`MaxImageResponseBytes` when your workload needs different limits.

Provide exactly one of `StructuredRequest.Input` or ordered `Messages`. Requests are
snapshotted before credential resolution; do not mutate caller collections during that
snapshot. Observers, DTO constructors and setters are host code. Cancellation cannot
forcibly interrupt synchronous host code. See [execution](EXECUTION.md) for budgets,
streaming and callback limits and [usage](USAGE.md) for observer delivery.
