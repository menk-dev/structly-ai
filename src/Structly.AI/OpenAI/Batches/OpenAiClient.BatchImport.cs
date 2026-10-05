using System.Net;
using System.Text.Json;

namespace Structly.AI.OpenAI;

/// <inheritdoc/>
public sealed partial class OpenAiClient
{
    static void ValidateManifest(BatchManifest manifest, string endpoint)
    {
        if(manifest.Version != 1 || manifest.Endpoint != endpoint || manifest.Items is null || manifest.Items.Count is < 1 or > 50000)
            throw new ArgumentException("Invalid manifest version, endpoint or item count.");

        if(endpoint == "/v1/embeddings" && manifest.Items.Sum(x => (long)x.InputCount) > 50000)
            throw new ArgumentException("Embedding batches support at most 50,000 inputs.");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var executions = new HashSet<Guid>();
        string? model = null;
        for(var i = 0; i < manifest.Items.Count; i++)
        {
            var item = manifest.Items[i];
            if(item.Position != i || String.IsNullOrWhiteSpace(item.CustomId) || item.CustomId.Length > 64 || !ids.Add(item.CustomId) || item.ExecutionId == Guid.Empty || !executions.Add(item.ExecutionId) || String.IsNullOrWhiteSpace(item.RequestedModel))
                throw new ArgumentException("Invalid batch item identity.");

            model ??= item.RequestedModel;
            if(model != item.RequestedModel)
                throw new ArgumentException("A batch must use one resolved model.");

            if(endpoint == "/v1/embeddings" && (item.InputCount is < 1 or > 2048 || item.Dimensions is <= 0))
                throw new ArgumentException("Invalid embedding manifest.");
        }
    }

    /// <summary>Imports terminal embedding results in original order and observes reported per-item usage.</summary>
    public Task<StructuredResult<IReadOnlyList<StructuredResult<IReadOnlyList<IReadOnlyList<float>>>>>> ImportEmbeddingBatchResultsAsync(string batchId, BatchManifest manifest, BatchOperationOptions? options = null, CancellationToken cancellationToken = default)
        => ImportBatch(batchId, manifest, "/v1/embeddings", options, (item, root, execution) => ValueTask.FromResult(ReadEmbeddings(root, item.InputCount, item.Dimensions, execution)), null, cancellationToken);

    /// <summary>Checks persisted schema fingerprints before importing typed results and reported per-item usage.</summary>
    public Task<StructuredResult<IReadOnlyList<StructuredResult<T>>>> ImportResponseBatchResultsAsync<T>(string batchId, BatchManifest manifest, StructuredTask<T> task, BatchOperationOptions? options = null, CancellationToken cancellationToken = default)
        => ImportBatch<T>(batchId, manifest, "/v1/responses", options, (item, root, execution) =>
        {
            execution.Metadata = execution.Metadata with { SchemaName = task.SchemaName };
            var request = new StructuredRequest { OpenAi = new() { CaptureOutputText = item.CaptureOutputText } };
            return ProcessResponse(root, request, execution, (text, metadata) => task.ReadOutput(text, item.Vocabularies?.ToDictionary(x => x.Key, x => (IReadOnlyList<string>)x.Value), metadata));
        }, item =>
        {
            if(Fingerprint(task.CreateSchema(item.Vocabularies?.ToDictionary(x => x.Key, x => (IReadOnlyList<string>)x.Value))) != item.SchemaFingerprint)
                throw new ArgumentException("Batch schema contract mismatch.");
        }, cancellationToken, task.SchemaName);

    Task<StructuredResult<IReadOnlyList<StructuredResult<T>>>> ImportBatch<T>(string batchId, BatchManifest manifest, string endpoint, BatchOperationOptions? options,
        Func<BatchManifestItem, JsonElement, OpenAiExecution, ValueTask<StructuredResult<T>>> process, Action<BatchManifestItem>? validate, CancellationToken caller, string? schemaName = null)
        => BatchOperation(options, "ImportBatch", async execution =>
        {
            try
            {
                manifest = BatchManifest.FromJson(manifest.ToJson());
                ValidateManifest(manifest, endpoint);
                foreach(var item in manifest.Items)
                    validate?.Invoke(item);
            }
            catch(ArgumentException)
            {
                return InvalidOptions<IReadOnlyList<StructuredResult<T>>>(execution);
            }

            execution.Metadata = execution.Metadata with { BatchId = batchId, SchemaName = schemaName };
            var job = await GetBatch(batchId, options, execution).ConfigureAwait(false);
            if(!job.IsSuccess)
                return StructuredResult<IReadOnlyList<StructuredResult<T>>>.Failure(job.Error!, execution.Metadata);

            if(!job.Value!.IsTerminal || job.Value.Endpoint != endpoint)
                return InvalidOptions<IReadOnlyList<StructuredResult<T>>>(execution);

            var index = manifest.Items.ToDictionary(x => x.CustomId, StringComparer.Ordinal);
            var results = new Dictionary<string, StructuredResult<T>>(StringComparer.Ordinal);
            long downloaded = 0;
            foreach(var file in new[] { job.Value.OutputFileId, job.Value.ErrorFileId }.Where(x => x is not null))
            {
                var imported = await BatchHttp(HttpMethod.Get, "files/" + RemoteId(file!) + "/content", null, options, execution, async response =>
                {
                    using var source = await execution.Await(response.Content.ReadAsStreamAsync(execution.Token), s => s.Dispose()).ConfigureAwait(false);
                    var chunk = new byte[8192];
                    using var line = new MemoryStream();
                    int count;
                    while((count = await execution.Await(source.ReadAsync(chunk, execution.Token).AsTask()).ConfigureAwait(false)) != 0)
                    {
                        downloaded += count;
                        if(downloaded > (options?.MaxDownloadBytes ?? 256L * 1024 * 1024))
                            throw new JsonException();

                        for(var i = 0; i < count; i++)
                        {
                            if(chunk[i] == 10)
                            {
                                if(line.Length > 0)
                                    await ReadLine().ConfigureAwait(false);

                                line.SetLength(0);
                            }
                            else
                            {
                                if(line.Length >= _options.MaxResponseBytes)
                                    throw new JsonException();

                                line.WriteByte(chunk[i]);
                            }
                        }
                    }

                    if(line.Length > 0)
                        await ReadLine().ConfigureAwait(false);

                    return true;
                    async Task ReadLine()
                    {
                        using var document = JsonDocument.Parse(line.ToArray());
                        var root = document.RootElement;
                        var id = Text(root, "custom_id");
                        if(id is null || !index.TryGetValue(id, out var item) || results.ContainsKey(id))
                            throw new JsonException();

                        using var itemExecution = new OpenAiExecution(BatchControls(options), _options, caller);
                        var envelope = Property(root, "response");
                        var body = Property(envelope, "body");
                        itemExecution.Metadata = new()
                        {
                            Operation = endpoint == "/v1/embeddings" ? "Embeddings" : "Structured",
                            Provider = "openai",
                            ExecutionId = item.ExecutionId,
                            CorrelationId = item.CorrelationId,
                            RequestedModel = item.RequestedModel,
                            BatchId = batchId,
                            BatchCustomId = id,
                            IsBatch = true,
                            ResolvedModel = Text(body, "model"),
                            ResponseId = Text(body, "id"),
                            ProviderRequestId = Text(envelope, "request_id"),
                            Usage = ParseUsage(body, itemExecution.Warnings),
                            RawResponse = item.CaptureRawResponse && body.ValueKind != JsonValueKind.Undefined ? body : null,
                        };
                        StructuredResult<T> result;
                        var status = Property(envelope, "status_code");
                        if(Property(root, "error").ValueKind == JsonValueKind.Object)
                            result = LocalFailure<T>(itemExecution, StructuredErrorKind.BatchItemFailed);
                        else if(status.ValueKind != JsonValueKind.Number || !status.TryGetInt32(out var code) || code is < 100 or > 599)
                            throw new JsonException();
                        else if(code is < 200 or >= 300)
                            result = StructuredResult<T>.Failure(new() { Kind = ClassifyStatus(code), Message = "Batch item HTTP failure.", HttpStatusCode = (HttpStatusCode)code, IsTransient = ClassifyStatus(code) == StructuredErrorKind.ProviderUnavailable || ClassifyStatus(code) == StructuredErrorKind.RateLimited && Text(Property(body, "error"), "code") != "insufficient_quota" }, itemExecution.Metadata);
                        else
                            result = await process(item, body, itemExecution).ConfigureAwait(false);

                        if(itemExecution.Metadata.Usage is { } usage && (options?.UsageObserver ?? _options.UsageObserver) is { } observer)
                            await execution.Callback(token => observer(new() { Provider = "openai", RequestedModel = item.RequestedModel, Usage = usage, Metadata = itemExecution.Metadata, Succeeded = result.IsSuccess, FailureKind = result.Error?.Kind, CallerCancelled = caller.IsCancellationRequested }, token), TimeSpan.FromSeconds(5), "UsageObserver").ConfigureAwait(false);

                        results.Add(id, result.IsSuccess ? StructuredResult<T>.Success(result.Value!, itemExecution.Metadata, itemExecution.Warnings) : StructuredResult<T>.Failure(result.Error!, itemExecution.Metadata, itemExecution.Warnings));
                    }
                }).ConfigureAwait(false);
                if(!imported.IsSuccess)
                    return StructuredResult<IReadOnlyList<StructuredResult<T>>>.Failure(imported.Error!, execution.Metadata, execution.Warnings);
            }

            var ordered = manifest.Items.Select(item => results.TryGetValue(item.CustomId, out var result) ? result : StructuredResult<T>.Failure(new() { Kind = StructuredErrorKind.IncompleteOutput, Message = "Terminal batch has no result for this item.", Issues = [new("$", "BatchResultMissing", "The terminal batch omitted this item.")] }, new() { ExecutionId = item.ExecutionId, SchemaName = schemaName, Provider = "openai", RequestedModel = item.RequestedModel, CorrelationId = item.CorrelationId, BatchId = batchId, BatchCustomId = item.CustomId, IsBatch = true })).ToArray();
            return StructuredResult<IReadOnlyList<StructuredResult<T>>>.Success(Array.AsReadOnly(ordered), execution.Metadata, execution.Warnings);
        }, caller);

}
