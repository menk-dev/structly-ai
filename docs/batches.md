# Batch files and results

Batch contracts and methods are in `Structly.AI.OpenAI`. The library supports batch Files
operations, embedding batches and homogeneous typed Responses batches. It does not schedule,
poll, persist jobs or implement retries. Applications manage remote file deletion explicitly.
The provider uses a `24h` completion window. See the
[provider Batch guide](https://developers.openai.com/api/docs/guides/batch).

## Prepare and submit

### 1. Prepare the requests

```csharp
var prepared = client.PrepareEmbeddingBatch([
    new("document-1", new()
    {
        Inputs = ["First document"],
        ModelSelection = new() { ModelId = configuredEmbeddingModel },
        Dimensions = 256,
        CorrelationId = "tenant-1"
    })
]);
```

### 2. Save the JSONL and manifest

```csharp
await using (var jsonl = File.Create("embedding-batch.jsonl"))
    await prepared.WriteJsonlAsync(jsonl, cancellationToken);

await File.WriteAllTextAsync("manifest.json", prepared.Manifest.ToJson(), cancellationToken);
```

### 3. Submit and retain recovery IDs

```csharp
var submission = await client.SubmitBatchAsync(prepared, cancellationToken: cancellationToken);
if (!submission.IsSuccess && submission.Metadata.UploadedFileId is { } uploadedFile)
    SaveFileForRecovery(uploadedFile);

var job = submission.EnsureSuccess();
SaveJobId(job.Id);
```

Application functions in this example persist IDs. If creation fails after upload, metadata
retains `UploadedFileId`; the library does not retry or silently delete it. Persist the manifest
before submission and persist the returned batch ID with it. Manifests contain no credentials
or delegates. Correlation and vocabulary data are application-owned and may be sensitive.
Preparation checks every item before uploading: nonblank unique custom IDs (at most 64 characters),
one resolved model, at most 50,000 requests, at most 2,048 inputs per embedding request and
50,000 embedding inputs per job, and a 200 MiB serialized-file ceiling. Token limits still
require application/provider validation because this package has no tokenizer.

`BatchOperationOptions` supplies credentials, total timeout, correlation ID, idempotency key
for POST operations, an import observer and aggregate download ceiling. Items must not contain
credential resolvers, deadlines, observers or idempotency keys. Typed batch items also reject
streaming and progress callbacks. Preparation throws `ArgumentException` on invalid input.
Async operations return `StructuredResult<T>` and preserve caller cancellation exceptions.

## Restart and import

```csharp
var manifest = BatchManifest.FromJson(await File.ReadAllTextAsync("manifest.json", cancellationToken));
var imported = await client.ImportEmbeddingBatchResultsAsync(batchId, manifest,
    new() { UsageObserver = RecordUsageAsync }, cancellationToken);

foreach (var item in imported.EnsureSuccess())
    SaveEmbeddingOrFailure(item.Metadata.BatchCustomId!, item);
```

Import requires a terminal status (`completed`, `failed`, `expired` or `cancelled`). It reads
both available output and error files, preserving partial terminal results. Results return in
original input order and correlate by custom ID, not file order. Unknown or duplicate IDs fail
the outer import as `InvalidResponse`. Missing items return `IncompleteOutput` with a
`BatchResultMissing` issue. Provider item errors without HTTP status use `BatchItemFailed`.

Usage is captured before vector or typed validation. Metadata includes `BatchId`,
`BatchCustomId`, `IsBatch`, and the execution ID generated once during preparation. Each import
can repeat usage events; use an application database unique key on `ExecutionId` to deduplicate
durably. An import observer overrides the client observer. Transport operations emit no usage.
Malformed files fail the outer result without undoing callbacks already delivered. JSONL is
parsed incrementally with `MaxResponseBytes` per line and a default 256 MiB aggregate ceiling
across both downloads; set `MaxDownloadBytes` when needed.

## Typed Responses

```csharp
var prepared = client.PrepareResponseBatch(task, [
    new("ticket-1", new() { Input = "Invoice charged twice", Vocabularies = vocabularies })
]);
var results = await client.ImportResponseBatchResultsAsync(batchId,
    BatchManifest.FromJson(prepared.Manifest.ToJson()), task,
    new() { UsageObserver = RecordUsageAsync }, cancellationToken);
```

Each job uses one task and DTO type. Items support request instructions, images, vocabularies,
model selection and non-streaming provider options. The manifest stores vocabulary snapshots,
capture settings and a SHA-256 fingerprint of the emitted schema. Import regenerates schemas
with the supplied task and rejects mismatches before HTTP processing or usage observation.
Refusal, incomplete output and strict validation follow synchronous Responses behavior.

Use `UploadBatchFileAsync`, `GetFileAsync`, `DownloadFileContentAsync`, `DeleteFileAsync`,
`CreateBatchAsync`, `GetBatchAsync`, `ListBatchesAsync` and `CancelBatchAsync` for explicit
lifecycle control. Download writes to the supplied stream and leaves it open. Upload likewise
leaves its source open. The library disposes its HTTP responses and owned streams and uses
the configured API directory and HTTP handler. File support is limited to batch requirements.
