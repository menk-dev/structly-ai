# Mistral

Install `Structly.AI.Mistral` for typed structured output and free text through
[Mistral chat completions](https://docs.mistral.ai/api). The package depends only on
`Structly.AI`; hosting is optional.

```csharp
using Structly.AI;
using Structly.AI.Mistral;

using var http = new HttpClient();
var client = new MistralClient(http, new MistralOptions
{
    DefaultModel = new() { ModelId = "mistral-small-latest" },
    UseEnvironmentApiKey = true,
});
var task = StructuredTask.Create<Answer>("Extract the answer.");
var result = await client.ExecuteAsync(task, "The answer is forty-two.");
var answer = result.EnsureSuccess();
var text = await client.GenerateTextAsync(new TextRequest
{
    Request = new() { Input = "Explain the answer briefly." },
});

public sealed record Answer(string Value);
```

The caller disposes the HTTP client. Options and model profiles are snapshotted at
construction. Request credentials override task credentials, which override client
credentials. Client configuration accepts a resolver, a configured API key, or explicit
environment lookup through `MISTRAL_API_KEY`. Validation does not resolve credentials.

For hosting, install `Structly.AI.Hosting` and select the provider:

```csharp
using Structly.AI.Hosting;
using Structly.AI.Mistral;

services.AddStructlyAi(configuration, builder =>
{
    builder.ConfigureMistralProvider(options =>
    {
        options.DefaultModel = new() { ModelId = "mistral-small-latest" };
        options.UseEnvironmentApiKey = true;
    });
    builder.AddTask<Answer>("answer", "Extract the answer.");
});
```

Hosting binds the `Structly:Mistral` section. Model IDs must support the requested
operation; the library does not maintain a model capability catalog.

Typed execution sends a JSON schema and validates returned JSON locally. It supports
bound output, runtime vocabularies, generated output guidance, model profiles, token caps,
total deadlines, caller cancellation, and best-effort usage observers. Provider IDs and
reported token counts are retained in result metadata. Provider error bodies are never
copied into diagnostics. Applications implement retries.

This initial provider supports non-streaming text messages with system, user, and
assistant roles. Streaming, progress callbacks, reasoning controls, developer messages,
image parts, and provider-specific request subclasses return `InvalidRequest` before
credential resolution. Embeddings, images, files, batches, and provider conversations
are not implemented. Text generation rejects typed output settings. The default
response envelope limit is 16 MiB.
