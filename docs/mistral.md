# Mistral

Install `Structly.AI.Mistral` for structured output, text, vision, embeddings, images,
conversations and batches. The package depends only on
`Structly.AI`; hosting is optional.

```csharp
using Structly.AI;
using Structly.AI.Mistral;

using var http = new HttpClient();
http.Timeout = Timeout.InfiniteTimeSpan;
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

## Feature comparison and official sources

The original package implemented buffered text chat and structured output. The following
comparison covers the existing Structly features that have documented Mistral equivalents.
Official documentation and the official generated SDK models were checked on 2026-10-06.
Model-specific support remains the provider's responsibility; accepting an option locally
does not promise that every model implements it.

| Existing feature | Mistral implementation | Official source |
| --- | --- | --- |
| Typed JSON schema output | `ExecuteAsync`, including bound output and local contract validation | [Custom structured output](https://docs.mistral.ai/studio/conversations/structured-output/custom) |
| Free text, ordered messages, token caps | `GenerateTextAsync`, system/user/assistant messages, `max_tokens` | [Chat API](https://docs.mistral.ai/api/endpoint/chat) |
| Streaming and progress | `Stream`, ordered text deltas, final validation and inactivity deadlines | [Chat streaming protocol](https://docs.mistral.ai/api/endpoint/chat) |
| Image input from HTTPS or bytes | User `ImagePart`, including base64 URLs | [Vision](https://docs.mistral.ai/studio/conversations/vision) |
| Image detail | `Auto`, `Low`, `High` sent as `image_url.detail` | [Official image URL model](https://github.com/mistralai/client-python/blob/main/src/mistralai/client/models/imageurl.py), [detail values](https://github.com/mistralai/client-python/blob/main/src/mistralai/client/models/imagedetail.py) |
| Reasoning effort | Core `Low`, `Medium`, `High` values sent as `reasoning_effort` | [Chat API](https://docs.mistral.ai/api/endpoint/chat) |
| Thinking content | Only answer text is returned; full thinking blocks are preserved in conversation history | [Reasoning guide](https://docs.mistral.ai/studio/conversations/reasoning) |
| Prompt cache key and cached usage | `MistralRequest.PromptCacheKey`, `Usage.CachedInputTokens` | [Prompt caching](https://docs.mistral.ai/studio/conversations/advanced/prompt-caching) |
| Provider metadata | `MistralRequest.Metadata` | [Chat API](https://docs.mistral.ai/api/endpoint/chat) |
| Typed continuation and branches | `CreateConversation` replays local history | [Chat messages](https://docs.mistral.ai/studio/conversations/chat-completion) |
| Provider-stored continuation and branches | `CreateStoredConversation` uses the beta start/restart endpoints | [Conversations API](https://docs.mistral.ai/api/endpoint/beta/conversations) |
| Float embeddings and dimensions | `EmbedAsync`, `output_dimension`, indexed immutable vectors | [Embeddings API](https://docs.mistral.ai/api/endpoint/embeddings) |
| Image generation | `GenerateImagesAsync` uses the beta image tool, then downloads its PNG | [Image generation](https://docs.mistral.ai/studio/agents/agent-tools/image_generation), [conversation tools](https://docs.mistral.ai/api/endpoint/beta/conversations) |
| File upload, retrieval, download, deletion | Batch file methods and bounded content downloads | [Files API](https://docs.mistral.ai/api/endpoint/files) |
| Batch preparation, submission and import | Typed chat and embedding JSONL, persisted manifests and schema fingerprints | [Batch processing](https://docs.mistral.ai/studio/batch-processing) |
| Batch create, retrieve, list and cancel | `/batch/jobs`, numbered pages and Mistral terminal states | [Batch API](https://docs.mistral.ai/api/endpoint/batch) |
| Retained identities and token usage | Response/model IDs, native conversation IDs and reported counts | [Chat API](https://docs.mistral.ai/api/endpoint/chat), [conversation response model](https://github.com/mistralai/client-python/blob/main/src/mistralai/client/models/conversationresponse.py) |

Schemas, vocabularies, generated instructions, credential precedence, hosting, model
profiles, correlation IDs, capture, deadlines, cancellation, usage observers and error
categories are library features. They are applied locally around these documented APIs.

The following OpenAI controls have no matching documented Mistral control in the linked
API specifications. They are not silently translated:

| Control | Mistral behavior and evidence |
| --- | --- |
| Developer message role | Rejected. The [chat message union](https://docs.mistral.ai/api/endpoint/chat) has system, user, assistant and tool roles; use system instructions. |
| Reasoning summaries | `IncludeReasoningSummary` is rejected. [Thinking chunks](https://docs.mistral.ai/studio/conversations/reasoning) contain raw traces, not the summary contract. |
| Cache modes, TTL, breakpoints, comparison and prewarming | Unsupported. Mistral documents [prefix caching with a key](https://docs.mistral.ai/studio/conversations/advanced/prompt-caching), rather than these cache controls. Cache markers are rejected. |
| OpenAI `PreviousResponseId` and `Store` request fields | Use Mistral stored conversations, which have [conversation and entry IDs](https://docs.mistral.ai/api/endpoint/beta/conversations). OpenAI request subclasses are rejected. |
| Image count, explicit dimensions, quality, background and encoding controls | Only one PNG with automatic settings is supported. The [Mistral image tool](https://docs.mistral.ai/studio/agents/agent-tools/image_generation) returns generated files rather than exposing the standalone image request controls. |
| Idempotency header | Unsupported; the linked API specifications do not document the OpenAI header contract. Embedding/image requests with a key fail locally. |

## Streaming, reasoning and capture

```csharp
var streamed = await client.ExecuteAsync(task, new MistralRequest
{
    Input = "The answer is forty-two.",
    Stream = true,
    InactivityTimeout = TimeSpan.FromSeconds(15),
    ModelSelection = new() { ModelId = "mistral-small-latest", ReasoningEffort = ReasoningEffort.High },
    PromptCacheKey = "answer-session",
    CaptureOutputText = true,
    Progress = (item, token) => DisplayAsync(item, token)
});
```

`DisplayAsync` is an application callback. Streaming chat requires a terminal finish
reason and `[DONE]`; native conversations require `conversation.response.done`.
Thinking is excluded from progress and output. `CaptureRawResponse` is available for
buffered chat and embeddings. Chat streaming rejects it because the protocol supplies
deltas rather than a complete final envelope. Raw capture can contain reasoning traces.
Text generation rejects vocabularies and typed output guidance.

The response limit is 16 MiB, including all SSE bytes. Inactivity resets for complete SSE
lines, including comments. `MistralOptions.InactivityTimeout` applies only to streaming;
per-call settings override it. `TimeProvider` controls deadlines and retry dates.
Callbacks are best effort: progress is limited to one second and usage to five seconds,
within the total budget. Callback and malformed usage warnings retain sanitized wording.
Late transport responses are disposed if execution stops waiting for them.

## Local and stored conversations

```csharp
var local = client.CreateConversation(task.BindOutput());
await local.ExecuteAsync(new() { Input = "Extract the answer." });
var branch = local.ChangeOutput(task.BindOutput());

var stored = client.CreateStoredConversation(task);
var turn = await stored.ExecuteAsync(new() { Input = "Extract the answer.", Stream = true });
string? conversationId = turn.Metadata.ConversationId;
string? entryId = turn.Metadata.ResponseId;
```

Local conversations retain input and complete assistant content in memory. Streamed
thinking blocks are reassembled with their signatures before replay. Stored conversations
use `store=true` and restart from the last successful entry, so failures and parallel
branches do not change the selected position. They also retain local history; changing
the model, instructions or generated guidance starts a new stored conversation with that
history. Output-schema changes use the current schema on every turn. Both modes accept
only new user messages and advance after the entire operation succeeds. Stored turns can
be retained and billed even when local validation or cancellation fails.

## Embeddings and images

```csharp
using Structly.AI.Embeddings;
using Structly.AI.Imaging;

var embeddings = await client.EmbedAsync(new EmbeddingRequest
{
    Inputs = ["first document", "second document"],
    ModelSelection = new() { ModelId = "mistral-embed" }
});
var image = await client.GenerateImagesAsync(new ImageGenerationRequest
{
    Prompt = "Generate an orange cat in an office.",
    ModelSelection = new() { ModelId = "mistral-medium-latest" }
});
byte[] png = image.EnsureSuccess()[0].ToBytes();
```

Embedding dimensions map to `output_dimension`; the request explicitly selects float
encoding and float output. Models must support the chosen dimension. Results are ordered
by index and checked for complete unique indexes, equal lengths and finite values.
`EmbeddingProfiles` and `ImageProfiles` are separate from chat `Profiles` and are copied
at construction. Embedding/image selections reject reasoning effort.

Image selection names a Mistral conversation model with access to image generation.
Generation sends `store=false`, uses no persistent agent, and downloads the returned file
through the authenticated Files API. One PNG must be returned. The library checks its
signature without decoding the image. Image downloads default to a 128 MiB limit,
configured by `MaxImageResponseBytes`. Failure to produce or download the image retains
reported generation usage. Tool-generated files are not deleted automatically.

## Files and batches

```csharp
var prepared = client.PrepareResponseBatch(task,
[
    new ResponseBatchItem("first", new() { Input = "The answer is forty-two." }),
    new ResponseBatchItem("second", new() { Input = "The answer is seven." })
]);
string savedManifest = prepared.Manifest.ToJson();
var submitted = await client.SubmitBatchAsync(prepared);
var imported = await client.ImportResponseBatchResultsAsync(
    submitted.EnsureSuccess().Id, BatchManifest.FromJson(savedManifest), task);
```

Wait for a terminal job before importing; the library does not poll or retry.
`PrepareEmbeddingBatch` and `ImportEmbeddingBatchResultsAsync` provide the embedding
equivalents. Batch types are in `Structly.AI.Mistral`, independent of OpenAI batch types.
Jobs use one model, JSONL rows contain `custom_id` and `body`, and job creation supplies
`model`, `endpoint` and `input_files`. Preparation limits files to 512 MiB and jobs to
one million requests. Custom IDs are locally limited to 64 characters.

Manifests retain original order, execution IDs, vocabularies, capture settings and schema
fingerprints. Imports validate contracts before credentials, reject duplicate/unknown
results and preserve missing-item failures and partial cancellation results. Usage is
observed per item and summarized with coverage. Aggregate downloads default to 256 MiB;
each JSONL result is also bounded by `MaxResponseBytes`.

Use `UploadBatchFileAsync`, `GetFileAsync`, `DownloadFileContentAsync` and `DeleteFileAsync`
for explicit file management. Caller streams remain open; failed downloads can leave a
partial destination. `CreateBatchAsync` takes a model selection as well as the file and
endpoint. `ListBatchesAsync` takes `page` and `pageSize`, not an OpenAI cursor. Submission
retains `Metadata.UploadedFileId` on creation failure and performs no implicit cleanup.
