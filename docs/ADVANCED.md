# Messages, caching, embeddings and images

All operations return `StructuredResult<T>`. They use the same rules for one HTTP attempt,
total timeouts, caller cancellation, per-call credentials, errors and usage callback limits.
See [execution](EXECUTION.md). You can inspect a schema or generate output instructions
without credentials or provider calls.

## Messages and output instructions

Set either `StructuredRequest.Input` or `Messages`. `Input` is nonblank text representing
one user message. It is sent as a string unless generated output instructions need to be
added to a message. `Messages` is a nonempty ordered list. Supported roles are `System`,
`Developer`, `User` and `Assistant`. Each message must have content, and each `TextPart`
must contain nonblank text. Assistant text is sent as `output_text`; other roles use `input_text`.

`ImagePart` is allowed only in user messages. Its `Url` must be an absolute HTTPS URL
without embedded credentials or a fragment, or a nonempty base64 PNG, JPEG, WebP or GIF
data URL. `Detail` accepts `Auto`, `Low` or `High`. The library checks URL and base64
syntax but does not download images. The provider checks image content and model support.

```csharp
var request = new StructuredRequest
{
    Messages =
    [
        new(MessageRole.Developer, [new TextPart("Use the supplied policy.")]),
        new(MessageRole.User,
        [
            new TextPart("Extract the ticket from this screenshot."),
            new ImagePart { Url = screenshotHttpsUrl, Detail = ImageDetail.High }
        ])
    ],
    Vocabularies = queues,
    OutputSpecification = new() { AdditionalInstructions = "Keep the summary concise." },
    OpenAi = new() { Store = true }
};
var result = await client.ExecuteAsync(task, request, cancellationToken);
```

`CreateOutputSpecification(options, vocabularies)` generates the same output instructions
without sending a request. `IncludeFields` and `IncludeExample` default to true. Property
paths, descriptions, schema and example use the resolved serialization rules, including
renamed properties, constraints and vocabulary values. Instructions are appended to the
last user message after its existing content. Earlier messages and cache markers are unchanged.
A nonempty specification requires a user message to append to.

### Generated examples

Generated examples use null for nullable values, the first enum or vocabulary value in
ordinal order, scalar values within constraints, and the minimum collection length.
Every example must pass `ReadOutput`, including deserialization.

For nonnullable patterns or formats, unique collections with multiple items, combined
constraints or large examples, you may need to supply `ExampleJson` or set
`IncludeExample=false`. Generation is limited to 10,000 nodes and 1 MiB of scalar text,
with at most 1,000 items per array and 100,000 characters per generated string with an
unconstrained format.

Supplied examples are also validated locally. Invalid examples are not sent. Inspection
throws `ArgumentException` for invalid output instructions; execution returns
`InvalidRequest` before reading credentials. Schema and vocabulary errors remain
`UnsupportedSchema`.

## Follow-up requests

Set `OpenAi.PreviousResponseId` to continue from an earlier response:

```csharp
var followUp = await client.ExecuteAsync(task, new StructuredRequest
{
    Input = "Here is the missing invoice detail.",
    Vocabularies = revisedQueues,
    OpenAi = new() { PreviousResponseId = result.Metadata.ResponseId, Store = false }
}, cancellationToken);
```

Your application must keep a successful response ID that remains available under the
same provider account. `Store` defaults to false. `Store=false` on the follow-up controls
storage of that response; it does not prevent using an earlier stored response.

Current instructions and schema are sent on every call. You can change the task type or
vocabularies, and the new output is checked against the new schema. The library does not
store conversations or add history automatically. Availability of stored responses depends
on the provider's retention rules.

## Free text

`TextRequest.Request` contains input and execution settings; `Instructions` is optional.
`Vocabularies` and `OutputSpecification` are rejected because free text has no typed
schema. The HTTP request omits `text.format`. Streaming, text and image messages, follow-up
requests, profiles, cache settings and capture settings work as they do for structured output.

Completed text must be nonblank. Refused, incomplete and failed responses use the same
error categories and retain any available usage counts.

```csharp
var text = await client.GenerateTextAsync(new TextRequest
{
    Instructions = "Summarize the report for an operator.",
    Request = new() { Input = report, Stream = true, TotalTimeout = TimeSpan.FromSeconds(60) }
}, cancellationToken);
string summary = text.EnsureSuccess();
```

## Cache settings

`PromptCacheKey` is optional metadata used by the provider for accounting and request routing.
Its effect depends on the model; it does not guarantee reuse. Configure `CacheCompatibility`
using the exact resolved model ID. `Modern` enables mode, TTL, breakpoints, comparison and
prewarming. `Legacy` enables `CacheRetention`. Unknown compatibility causes advanced cache
settings to fail locally. Configure this from the provider's documented model support;
the library does not infer support from model names.

```csharp
var options = new OpenAiClientOptions
{
    DefaultModel = new() { ModelId = configuredResponseModel },
    CredentialResolver = Credentials.FromEnvironment("OPENAI_API_KEY"),
    CacheCompatibility = new Dictionary<string, OpenAiCacheCompatibility>
    {
        [configuredResponseModel] = OpenAiCacheCompatibility.Modern
    }
};
var cachedRequest = new StructuredRequest
{
    Messages = [new(MessageRole.User,
    [
        new TextPart(stableContext) { CacheBreakpoint = true },
        new TextPart(changingQuestion)
    ])],
    Vocabularies = queues,
    OpenAi = new()
    {
        PromptCacheKey = accountCacheKey,
        Cache = new() { Mode = OpenAiCacheMode.Explicit, Ttl = "30m" }
    }
};
```

Modern settings use `prompt_cache_options` and content-part `prompt_cache_breakpoint`.
Explicit mode marks the exact cache boundary. It requires at least one marker and allows
at most four. Implicit or default mode allows three explicit markers, reserving one slot
for the implicit write. Assistant output and top-level instructions cannot have markers.
Use a developer text message to mark reusable instructions. If supplied, TTL must be `30m`.

Legacy retention accepts `in_memory` or `24h`. The library rejects combinations of legacy
and modern settings, even if a provider might accept them.

`Cache.ComparisonResponseId` requests diagnostics; it does not load conversation history.
`Metadata.CacheDiagnostics` keeps unknown `Type` and `Reason` strings and independently
reported comparison counts. Malformed counts add warnings without replacing output.
`CachedInputTokens` and `CacheWriteTokens` are reported usage counts. Keys and diagnostics
do not guarantee a cache hit or a price. Changes to schemas, vocabularies, instructions,
models or reasoning settings can affect prefix reuse.

### Prewarming

`PrewarmAsync` is a separate operation that returns metadata without output.
`OpenAiPrewarmRequest` contains `Request` and optional `Instructions`. The typed overload
also uses the task's instructions, model, credentials and current schema and vocabularies.
It sends `prompt_cache_options.prewarm=true` and requires a completed response with empty
output. It does not deserialize an output object.

```csharp
var warm = await client.PrewarmAsync(task, new OpenAiPrewarmRequest
{
    Request = cachedRequest
}, cancellationToken);
// Send the real operation with the same reusable prefix and schema.
var answer = await client.ExecuteAsync(task, cachedRequest, cancellationToken);
```

Prewarming requires `Modern` compatibility. It rejects streaming, progress callbacks,
reasoning summaries, output token limits, output instructions and capture, `Store=true`,
and follow-up requests. Untyped prewarming also rejects vocabularies. Prewarming can incur
usage and returns reported metadata even on failure. It does not guarantee a cache write
or reuse on the next request.

These settings follow the provider's [prompt caching guide](https://developers.openai.com/api/docs/guides/prompt-caching)
and [diagnostics guide](https://developers.openai.com/api/docs/guides/prompt-caching/diagnostics),
checked on 2026-10-04. Minimum prefix lengths, retention, write availability and billing
depend on the provider and model.

## Embeddings and images

Embedding types are in `Structly.AI.Embeddings`; image types are in `Structly.AI.Imaging`.
Both operations require `ModelSelection` with either `ModelId` or `ProfileName`. Configure
their profiles in `OpenAiClientOptions.EmbeddingProfiles` and `ImageProfiles`. Structured
output, text and prewarming use `Profiles`. The same profile name may select different
models for different operations. Profile dictionaries are copied when the client is created.
Embedding and image profiles and requests reject reasoning effort settings.

```csharp
var embedded = await client.EmbedAsync(new EmbeddingRequest
{
    Inputs = ["first document", "second document"],
    ModelSelection = new() { ModelId = configuredEmbeddingModel },
    Dimensions = 1024
}, cancellationToken);
IReadOnlyList<IReadOnlyList<float>> vectors = embedded.EnsureSuccess();

var generated = await client.GenerateImagesAsync(new ImageGenerationRequest
{
    Prompt = "A simple floor plan with clearly labeled rooms.",
    ModelSelection = new() { ModelId = configuredImageModel },
    Size = ImageSize.Landscape,
    CustomDimensions = new(1536, 864), // Overrides the preset.
    Quality = ImageQuality.High,
    Background = ImageBackground.Transparent,
    Format = ImageFormat.Png
}, cancellationToken);
byte[] imageBytes = generated.EnsureSuccess()[0].ToBytes();
```

### Embeddings

An embedding request sends one nonempty text batch with `encoding_format=float` and an
optional positive `Dimensions` value. Results are reordered by index. Indexes must be
unique and complete. Vectors must be nonempty, contain finite values and have equal lengths.
If `Dimensions` is supplied, lengths must match it. Returned outer and inner lists are
read-only. `prompt_tokens` becomes `InputTokens`; missing output counts stay null.

### Images

Image requests send `n` from 1 to 10, quality, background, `output_format` and size.
Size presets map as follows:

| Preset | API value |
| --- | --- |
| Auto | auto |
| Square | 1024x1024 |
| Portrait | 1024x1536 |
| Landscape | 1536x1024 |
| Wide | 1536x864 |

Positive `CustomDimensions` override the preset. The provider checks model-specific
dimension restrictions. Transparent JPEG requests fail locally.

Output contains the requested number of base64 PNG, JPEG or WebP images in provider order.
The reported format, when present, and decoded file signatures must match the request.
Signature checks do not validate the entire image. `GeneratedImage` exposes `Base64Data`,
`MediaType` and `ToBytes`. The library does not fetch image URLs or include an image decoder.
Images use `MaxImageResponseBytes`, which defaults to 128 MiB. Other responses use
`MaxResponseBytes`, which defaults to 16 MiB.

### Usage and request settings

Usage is read before processing vectors or images. Invalid vectors, base64, counts or
formats therefore retain any reported usage and call the same usage observer.
Both request types expose `TotalTimeout`, `CredentialResolver`, `UsageObserver`,
`CorrelationId`, `IdempotencyKey` and `CaptureRawResponse`. They do not support streaming,
follow-up requests or cache settings. Missing image response IDs and model IDs remain
unknown. The library does not retry or split batches automatically.

Request and response formats follow the provider's [embeddings reference](https://developers.openai.com/api/reference/cli/resources/embeddings/methods/create)
and [image generation reference](https://developers.openai.com/api/reference/cli/resources/images/methods/generate),
checked on 2026-10-04. Choose models that support your dimensions and options and the
Image API's base64 and `output_format` settings. Unsupported model-specific combinations
return provider errors.

## Instructions and task identification

`StructuredTaskOptions.Instructions` supplies reusable task instructions.
Set `StructuredRequest.Instructions` to replace them for one call; null uses the task
instructions and whitespace is invalid. Typed prewarming uses the same request override,
so use identical instructions when prewarming and executing. Typed prewarming still rejects
`OpenAiPrewarmRequest.Instructions`; put the override on its `Request` instead.
For text and untyped prewarming, request instructions override the operation's instructions.

For additional source-specific instructions, use `StructuredRequest.Messages` with a
`MessageRole.Developer` text message followed by a user message. Developer messages are
additional input; they do not replace task instructions. `Messages` and `Input` are mutually exclusive.

Typed results and usage events include the task's resolved `StructuredMetadata.SchemaName`.
`Operation` identifies the operation category, such as `Structured` or `Prewarm`;
`CorrelationId` remains available for the application's individual call identifier.
Local `ReadOutput` results also include the schema name.

Omitting `StructuredRequest.OpenAi` or explicitly assigning null both use default provider
settings. For example, `OpenAi = useCache ? new() { PromptCacheKey = "source" } : null`
is supported.

Exact cache defaults classify `gpt-6-luna`, `gpt-6-sol` and `gpt-6-astra` as `Modern`.
Application `CacheCompatibility` entries override these defaults. Unknown IDs, including
suffix variants, require explicit configuration; model prefixes do not determine compatibility.
Modern controls apply to GPT-5.6 and later. See the
[provider prompt-caching guide](https://developers.openai.com/api/docs/guides/prompt-caching).

## Bound output and conversations

Bind runtime output settings once to keep the schema and generated guidance stable:

```csharp
var output = ticketTask.BindOutput(new()
{
    Vocabularies = vocabularies,
    OutputSpecification = new() { IncludeExample = true }
});
var conversation = client.CreateConversation(output);
await conversation.ExecuteAsync(new() { Input = "Extract this issue..." });
await conversation.ExecuteAsync(new() { Input = "Correct the reference..." });
var updated = conversation.ChangeOutput(
    ticketTask.BindOutput(new() { Vocabularies = updatedVocabularies }));
var planning = conversation.ChangeOutput(planTask.BindOutput());
```

Binding copies referenced vocabulary values, validates the effective schema, and generates
optional guidance before credentials or HTTP are involved. `CreateSchema()` returns a
detached schema; `ReadOutput()` validates against the captured values. You can also call
`client.ExecuteAsync(output, request)` directly; request vocabularies and output
specifications must be absent.

Conversation creation resolves instructions and model settings from conversation options,
the original task, and client defaults. Turns accept new user input and execution controls.
Messages must contain only user roles. Reasoning summaries configured at creation require
streaming on every turn.

OpenAI-backed conversations send `store: true`: responses are retained at OpenAI and later
turns reference the last successful response. Failures leave the local continuation position
unchanged, but may still have been stored or billed. Accounting and cancellation metadata
remain available. The library does not retry or restart unavailable provider history.

`ChangeOutput` returns an independent typed branch at the current position. It preserves
configuration and the original credential fallback, including when the new task specifies
other instructions, models, or credentials. `ChangeConfiguration(conversation.Configuration
with { Instructions = "Revised instructions" })` explicitly creates a configuration branch.
Both originals remain usable; branches advance independently. Overlapping operations on
the same conversation throw `InvalidOperationException`.

Existing task/request execution, manual continuation, batch, and prewarming APIs remain
available without migration. Use those advanced APIs for per-call structural overrides,
provider storage controls, cache controls, or raw capture. Binding and conversations make
no promises about cache writes, hits, or savings.
