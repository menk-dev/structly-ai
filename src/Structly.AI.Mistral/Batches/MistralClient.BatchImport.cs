using System.Net;
using System.Text.Json;

namespace Structly.AI.Mistral;

/// <inheritdoc/>
public sealed partial class MistralClient
{
    static void ValidateManifest(BatchManifest manifest, string endpoint)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if(endpoint is not ("/v1/chat/completions" or "/v1/embeddings") || manifest.Version != 1 || manifest.Endpoint != endpoint || manifest.Items is null || manifest.Items.Count is < 1 or > 1000000)
            throw new ArgumentException("Invalid manifest version, endpoint or item count.");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var executions = new HashSet<Guid>();
        string? model = null;
        for(var i = 0; i < manifest.Items.Count; i++)
        {
            var item = manifest.Items[i];
            if(item is null || item.Position != i || String.IsNullOrWhiteSpace(item.CustomId) || item.CustomId.Length > 64 || !ids.Add(item.CustomId) || item.ExecutionId == Guid.Empty || !executions.Add(item.ExecutionId) || String.IsNullOrWhiteSpace(item.RequestedModel))
                throw new ArgumentException("Invalid batch item identity.");

            model ??= item.RequestedModel;
            if(model != item.RequestedModel)
                throw new ArgumentException("A batch must use one resolved model.");

            if(endpoint == "/v1/embeddings" && (item.InputCount < 1 || item.Dimensions is <= 0))
                throw new ArgumentException("Invalid embedding manifest.");
        }
    }

    /// <summary>Imports terminal embedding results in original order and observes reported per-item usage.</summary>
    public Task<StructuredResult<BatchImportResult<IReadOnlyList<IReadOnlyList<float>>>>> ImportEmbeddingBatchResultsAsync(string batchId, BatchManifest manifest, BatchOperationOptions? options = null, CancellationToken cancellationToken = default)
        => ImportBatch(batchId, manifest, "/v1/embeddings", options, (item, root, execution) => ValueTask.FromResult(ReadEmbeddings(root, item.InputCount, item.Dimensions, execution)), null, cancellationToken);

    /// <summary>Checks persisted schema fingerprints before importing typed results and reported per-item usage.</summary>
    public Task<StructuredResult<BatchImportResult<T>>> ImportResponseBatchResultsAsync<T>(string batchId, BatchManifest manifest, StructuredTask<T> task, BatchOperationOptions? options = null, CancellationToken cancellationToken = default)
        => ImportBatch<T>(batchId, manifest, "/v1/chat/completions", options, (item, root, execution) =>
        {
            execution.Metadata = execution.Metadata with { SchemaName = task.SchemaName };
            var request = new MistralRequest { CaptureOutputText = item.CaptureOutputText };
            var output = task.BindOutput(new() { Vocabularies = item.Vocabularies?.ToDictionary(x => x.Key, x => (IReadOnlyList<string>)x.Value) });
            return ValueTask.FromResult(ReadChat(root, request, output, execution));
        }, item =>
        {
            if(Fingerprint(task.CreateSchema(item.Vocabularies?.ToDictionary(x => x.Key, x => (IReadOnlyList<string>)x.Value))) != item.SchemaFingerprint)
                throw new ArgumentException("Batch schema contract mismatch.");
        }, cancellationToken, task.SchemaName);

    Task<StructuredResult<BatchImportResult<T>>> ImportBatch<T>(string batchId, BatchManifest manifest, string endpoint, BatchOperationOptions? options,
        Func<BatchManifestItem, JsonElement, MistralExecution, ValueTask<StructuredResult<T>>> process, Action<BatchManifestItem>? validate, CancellationToken caller, string? schemaName = null)
        => BatchOperation(options, "ImportBatch", async execution =>
        {
            try
            {
                ArgumentNullException.ThrowIfNull(manifest);
                manifest = BatchManifest.FromJson(manifest.ToJson());
                ValidateManifest(manifest, endpoint);
                foreach(var item in manifest.Items)
                    validate?.Invoke(item);
            }
            catch(Exception exception) when(exception is ArgumentException or JsonException)
            {
                return execution.Failure<BatchImportResult<T>>(StructuredErrorKind.InvalidRequest);
            }

            execution.Metadata = execution.Metadata with { BatchId = batchId, SchemaName = schemaName };
            var job = await GetBatch(batchId, options, execution).ConfigureAwait(false);
            if(!job.IsSuccess)
                return StructuredResult<BatchImportResult<T>>.Failure(job.Error!, execution.Metadata);

            if(!job.Value!.IsTerminal || job.Value.Endpoint != endpoint || job.Value.Model != manifest.Items[0].RequestedModel)
                return execution.Failure<BatchImportResult<T>>(StructuredErrorKind.InvalidRequest);

            var index = manifest.Items.ToDictionary(x => x.CustomId, StringComparer.Ordinal);
            var results = new Dictionary<string, StructuredResult<T>>(StringComparer.Ordinal);
            long downloaded = 0;
            foreach(var file in new[] { job.Value.OutputFileId, job.Value.ErrorFileId }.Where(x => x is not null))
            {
                var imported = await BatchHttp(HttpMethod.Get, "files/" + RemoteId(file!) + "/content", null, options, execution, async response =>
                {
                    using var source = await execution.Await(response.Content.ReadAsStreamAsync(execution.Token), source => source.Dispose()).ConfigureAwait(false);
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
                                if(line.Length >= _maxResponseBytes)
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
                        var id = StringValue(root, "custom_id");
                        if(id is null || !index.TryGetValue(id, out var item) || results.ContainsKey(id))
                            throw new JsonException();

                        using var itemExecution = new MistralExecution(BatchControls(options), _clock, _totalTimeout, execution.Token);
                        var envelope = Property(root, "response");
                        var body = Property(envelope, "body");
                        itemExecution.Metadata = new()
                        {
                            Operation = endpoint == "/v1/embeddings" ? "Embeddings" : "Structured",
                            Provider = "Mistral",
                            ExecutionId = item.ExecutionId,
                            CorrelationId = item.CorrelationId,
                            RequestedModel = item.RequestedModel,
                            BatchId = batchId,
                            BatchCustomId = id,
                            IsBatch = true,
                            ResolvedModel = StringValue(body, "model"),
                            ResponseId = StringValue(body, "id"),
                            ProviderRequestId = StringValue(envelope, "request_id"),
                            Usage = ReadUsage(body, itemExecution.Warnings),
                            RawResponse = item.CaptureRawResponse && body.ValueKind != JsonValueKind.Undefined ? body : null,
                        };
                        StructuredResult<T> result;
                        var status = Property(envelope, "status_code");
                        if(Property(root, "error").ValueKind == JsonValueKind.Object)
                            result = itemExecution.Failure<T>(StructuredErrorKind.BatchItemFailed);
                        else if(status.ValueKind != JsonValueKind.Number || !status.TryGetInt32(out var code) || code is < 100 or > 599)
                            throw new JsonException();
                        else if(code is < 200 or >= 300)
                            result = StructuredResult<T>.Failure(new() { Kind = ClassifyStatus((HttpStatusCode)code), Message = "Batch item HTTP failure.", HttpStatusCode = (HttpStatusCode)code, IsTransient = ClassifyStatus((HttpStatusCode)code) == StructuredErrorKind.ProviderUnavailable || ClassifyStatus((HttpStatusCode)code) == StructuredErrorKind.RateLimited && StringValue(Property(body, "error"), "code") != "insufficient_quota" }, itemExecution.Metadata);
                        else
                        {
                            try
                            {
                                result = await process(item, body, itemExecution).ConfigureAwait(false);
                            }
                            catch(Exception exception) when(exception is JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException)
                            {
                                result = itemExecution.Failure<T>(StructuredErrorKind.InvalidResponse);
                            }
                        }

                        if(itemExecution.Metadata.Usage is { } usage && (options?.UsageObserver ?? _usageObserver) is { } observer)
                        {
                            await execution.Callback(token => observer(new() { Provider = "Mistral", RequestedModel = item.RequestedModel, Usage = usage, Metadata = itemExecution.Metadata, Succeeded = result.IsSuccess, FailureKind = result.Error?.Kind }, token), TimeSpan.FromSeconds(5), "UsageObserver").ConfigureAwait(false);
                        }

                        results.Add(id, result.IsSuccess ? StructuredResult<T>.Success(result.Value!, itemExecution.Metadata, itemExecution.Warnings) : StructuredResult<T>.Failure(result.Error!, itemExecution.Metadata, itemExecution.Warnings));
                    }
                }).ConfigureAwait(false);
                if(!imported.IsSuccess)
                    return StructuredResult<BatchImportResult<T>>.Failure(imported.Error!, execution.Metadata, execution.Warnings);
            }

            var ordered = manifest.Items.Select(item => results.TryGetValue(item.CustomId, out var result) ? result : StructuredResult<T>.Failure(new() { Kind = StructuredErrorKind.IncompleteOutput, Message = "Terminal batch has no result for this item.", Issues = [new("$", "BatchResultMissing", "The terminal batch omitted this item.")] }, new() { ExecutionId = item.ExecutionId, SchemaName = schemaName, Provider = "Mistral", RequestedModel = item.RequestedModel, CorrelationId = item.CorrelationId, BatchId = batchId, BatchCustomId = item.CustomId, IsBatch = true })).ToArray();
            var items = ordered.Select((result, position) => new BatchItemResult<T>(manifest.Items[position].CustomId, result)).ToArray();
            var summaries = SummarizeBatchUsage(items, execution.Warnings);
            return StructuredResult<BatchImportResult<T>>.Success(new(items, summaries), execution.Metadata, execution.Warnings);
        }, caller);

}
