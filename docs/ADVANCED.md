# Advanced runtime features

All operations return StructuredResult<T> and share one-attempt HTTP execution, total
budgets, caller cancellation, credential isolation, safe failures and bounded usage
observers. See [Execution guidance](EXECUTION.md). No live credentials or provider calls
are needed to inspect a schema or output specification.

## Messages, guidance and continuation

StructuredRequest accepts exactly one of Input (nonblank text shorthand) or Messages
(a nonempty ordered list). Text shorthand retains the provider's string wire form unless
guidance needs a message target; it means one user message. Supported roles are System,
Developer, User and Assistant. Every message needs nonempty content. TextPart requires
nonblank text; assistant text becomes output_text and other roles use input_text.

ImagePart is allowed only in user messages. Url must be absolute HTTPS without embedded
credentials/fragments, or a nonempty base64 PNG/JPEG/WebP/GIF data URL. Detail is Auto,
Low or High. The library validates URL/base64 syntax and never downloads an image; the
provider validates image content and model support.

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

CreateOutputSpecification(options, vocabularies) exposes the same guidance offline.
IncludeFields and IncludeExample default to true. The field paths, descriptions, full
schema and example come from the resolved serialization contract, including renamed
properties, constraints and vocabulary values. Guidance is appended after existing
content in the last user message; earlier messages and cache markers stay intact. A
nonempty specification requires a user target.

Generated examples choose nullable nulls, ordinal-first enum/vocabulary values, bounded
scalars and the minimum collection cardinality. Every example must pass ReadOutput,
including deserialization. Nonnullable patterns/formats, unique sets with multiple items,
combined constraints or large expansions can require ExampleJson or IncludeExample=false.
Generation is bounded to 10,000 nodes and 1 MiB of scalar text, with at most 1,000 items
per generated array and 100,000 characters per generated unconstrained-format string.
Caller examples also undergo local validation; no invalid sample is sent. Inspection
throws ArgumentException for invalid guidance; execution returns InvalidRequest before
credential resolution. Schema/vocabulary errors remain UnsupportedSchema.

Continuation uses OpenAi.PreviousResponseId explicitly:

```csharp
var followUp = await client.ExecuteAsync(task, new StructuredRequest
{
    Input = "Here is the missing invoice detail.",
    Vocabularies = revisedQueues,
    OpenAi = new() { PreviousResponseId = result.Metadata.ResponseId, Store = false }
}, cancellationToken);
```

The host must retain a successful prior response ID that is available under the same
provider account. Store defaults to false. Store=false on the follow-up controls that
response's persistence; it does not prevent using a stored earlier response. Current
instructions and schema are resent every time. Changing the task type or vocabularies
is supported, and current output is validated against the new contract. There is no
local conversation store, automatic history or retention guarantee.

## Free text

TextRequest composes response input/execution controls in Request and has optional
Instructions. Vocabularies and OutputSpecification are rejected because text has no
typed contract. The wire omits text.format. Streaming, multimodal messages, continuation,
model profiles, cache options and capture controls work as for structured responses.
Completed output must be nonblank; refusal/incomplete/provider failures keep their normal
categories and available usage.

```csharp
var text = await client.GenerateTextAsync(new TextRequest
{
    Instructions = "Summarize the report for an operator.",
    Request = new() { Input = report, Stream = true, TotalTimeout = TimeSpan.FromSeconds(60) }
}, cancellationToken);
string summary = text.EnsureSuccess();
```

## Cache controls and prewarm

PromptCacheKey is optional accounting/routing metadata. Its effect differs by model;
it never guarantees reuse. Configure CacheCompatibility by exact resolved model ID:
Modern enables mode/TTL/breakpoints/comparison/prewarm, Legacy enables CacheRetention.
Unknown compatibility rejects advanced controls locally. The caller must configure this
from actual provider/model support; the library does not guess from model names.

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

Modern controls use prompt_cache_options and content-part prompt_cache_breakpoint with
mode=explicit at the exact marked boundary. Explicit mode requires at least one marker
and allows at most four; implicit/default mode allows three explicit markers, reserving
the implicit write slot. Assistant output and top-level instructions cannot carry markers.
Use a developer text message to mark reusable instructions. TTL, when supplied, must be
30m. Legacy retention uses in_memory or 24h separately. Mixing legacy and modern options
is rejected by library policy, even where a provider might accept both.

Cache.ComparisonResponseId requests diagnostics; it does not load history. Metadata's
CacheDiagnostics preserves unknown Type/Reason strings and independent comparison counts.
Malformed diagnostic counts warn without masking output. CachedInputTokens and
CacheWriteTokens are reported usage; neither keys nor diagnostics promise a hit or prices.
Schema, vocabulary, instructions, model or reasoning changes can affect prefix reuse.

PrewarmAsync is a separate metadata-only operation. OpenAiPrewarmRequest composes Request
plus optional Instructions; the typed overload takes a task and uses its instructions,
model/credentials and current schema/vocabularies. It sends prompt_cache_options.prewarm=true
and requires a completed empty-output envelope. No DTO is deserialized.

```csharp
var warm = await client.PrewarmAsync(task, new OpenAiPrewarmRequest
{
    Request = cachedRequest
}, cancellationToken);
// Send the real operation with the same reusable prefix and schema.
var answer = await client.ExecuteAsync(task, cachedRequest, cancellationToken);
```

Prewarm rejects streaming, progress, reasoning summaries, output token caps, output
guidance/capture, Store=true and continuation. Untyped prewarm also rejects vocabularies.
Modern compatibility is required. The operation can incur usage and returns reported
metadata even on failure; it guarantees neither a cache write nor future reuse.

These controls follow the official [prompt caching guide](https://developers.openai.com/api/docs/guides/prompt-caching)
and [diagnostics guide](https://developers.openai.com/api/docs/guides/prompt-caching/diagnostics),
checked 2026-10-04. Minimum prefix lengths, retention, write availability and billing are
provider/model policy.

## Embeddings and images

Use Structly.AI.Embeddings for EmbeddingRequest and Structly.AI.Imaging for image types.
Both operations require ModelSelection (explicit ModelId or ProfileName). Profiles are
configured in OpenAiClientOptions.EmbeddingProfiles and ImageProfiles respectively;
response/text/prewarm profiles use Profiles. Identical profile names can choose different
models for each operation. Configuration dictionaries are snapshotted at construction.
Auxiliary profiles and requests reject reasoning effort, rather than silently ignoring it.

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

Embeddings send one nonempty text batch with encoding_format=float, optional positive
Dimensions, then reorder by unique complete indexes. Vectors must be finite, nonempty
and equal length, matching Dimensions when supplied. Returned outer and inner lists
are immutable. prompt_tokens maps to InputTokens; missing output counts stay null.

Images send n (1–10), quality, background, output_format and size. Presets map Auto,
Square, Portrait, Landscape and Wide to auto, 1024x1024, 1024x1536, 1536x1024 and 1536x864.
Positive CustomDimensions override the preset; model-specific dimension restrictions
are checked by the provider. Transparent JPEG fails locally. Output is base64 PNG/JPEG/
WebP, in provider order, with the requested count. Reported format (when present) and
decoded file signatures must match the request. This checks format signatures, not full
image integrity. GeneratedImage exposes Base64Data, MediaType and ToBytes; no URL fetch
or image decoder dependency is added. Images use MaxImageResponseBytes (128 MiB default),
other envelopes MaxResponseBytes (16 MiB default).

Usage is captured before vector/image processing. An invalid vector, base64, count or
format therefore preserves reported accounting and triggers the same bounded observer.
Both requests expose TotalTimeout, CredentialResolver, UsageObserver, CorrelationId,
IdempotencyKey and CaptureRawResponse. Streaming, continuation and cache controls are
excluded from these contracts. Missing image response IDs/models remain unknown rather
than being invented from the request. There are no automatic retries or batch splitting.

Wire behavior follows the official [embeddings reference](https://developers.openai.com/api/reference/cli/resources/embeddings/methods/create)
and [image generation reference](https://developers.openai.com/api/reference/cli/resources/images/methods/generate),
checked 2026-10-04. Use models supporting the requested dimensions/options and the Image
API base64/output_format contract; unsupported model-specific combinations are surfaced
as provider failures.
