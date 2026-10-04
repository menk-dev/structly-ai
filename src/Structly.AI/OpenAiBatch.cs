using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Structly.AI.Embeddings;

namespace Structly.AI.OpenAI;

public sealed partial class OpenAiClient
{
    static StructuredRequest BatchControls(BatchOperationOptions? options) => new()
    {
        TotalTimeout = options?.TotalTimeout,
        CredentialResolver = options?.CredentialResolver,
        CorrelationId = options?.CorrelationId,
        OpenAi = new() { IdempotencyKey = options?.IdempotencyKey }
    };

    Task<StructuredResult<T>> BatchOperation<T>(BatchOperationOptions? options, string name,
        Func<OpenAiExecution, Task<StructuredResult<T>>> action, CancellationToken token)
        => RunOperation(BatchControls(options), name, async execution =>
        {
            execution.CheckCancellation();
            try { ValidateCommonRequest(BatchControls(options)); if (options?.MaxDownloadBytes <= 0) throw new ArgumentException(); }
            catch (ArgumentException) { return InvalidOptions<T>(execution); }
            try { return await action(execution).ConfigureAwait(false); }
            catch (ArgumentException) { return InvalidOptions<T>(execution); }
        }, token);

    async Task<StructuredResult<T>> BatchHttp<T>(HttpMethod method, string endpoint, HttpContent? content,
        BatchOperationOptions? options, OpenAiExecution execution, Func<HttpResponseMessage, Task<T>> read)
    {
        using var message = new HttpRequestMessage(method, new Uri(_options.BaseAddress, endpoint)) { Content = content };
        string? credential;
        try
        {
            var resolver = options?.CredentialResolver ?? _options.CredentialResolver;
            credential = resolver is null ? null : await execution.Await(resolver(execution.Token).AsTask()).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (execution.Token.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is not OutOfMemoryException and not StackOverflowException) { return LocalFailure<T>(execution, StructuredErrorKind.Authentication); }
        if (String.IsNullOrWhiteSpace(credential)) return LocalFailure<T>(execution, StructuredErrorKind.CredentialsMissing);
        if (credential.Any(c => c < 33 || c > 126)) return LocalFailure<T>(execution, StructuredErrorKind.Authentication);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);
        if (options?.IdempotencyKey is { } key && method == HttpMethod.Post) message.Headers.Add("Idempotency-Key", key);
        try
        {
            execution.CheckCancellation();
            using var response = await execution.Await(_http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, execution.Token), r => r.Dispose()).ConfigureAwait(false);
            execution.Metadata = execution.Metadata with { ProviderRequestId = response.Headers.TryGetValues("x-request-id", out var ids) ? ids.FirstOrDefault() : null };
            if (!response.IsSuccessStatusCode)
            {
                var kind = ClassifyStatus((int)response.StatusCode);
                string? providerCode = null;
                try { providerCode = Text(Property(await ReadBatchEnvelope(response, execution).ConfigureAwait(false), "error"), "code"); }
                catch (Exception e) when (e is JsonException or ArgumentException or InvalidOperationException) { }

                var delay = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date is { } date ? date - _options.TimeProvider.GetUtcNow() : (TimeSpan?)null);
                return StructuredResult<T>.Failure(new()
                {
                    Kind = kind,
                    Message = $"Batch operation failed: {kind}.",
                    HttpStatusCode = response.StatusCode,
                    IsTransient = kind == StructuredErrorKind.ProviderUnavailable || kind == StructuredErrorKind.RateLimited && providerCode != "insufficient_quota",
                    RetryAfter = delay < TimeSpan.Zero ? TimeSpan.Zero : delay
                }, execution.Metadata);
            }
            var value = await read(response).ConfigureAwait(false);
            execution.CheckCancellation();
            return StructuredResult<T>.Success(value, execution.Metadata);
        }
        catch (OperationCanceledException) when (!execution.Token.IsCancellationRequested) { return LocalFailure<T>(execution, StructuredErrorKind.TransportFailure, true); }
        catch (HttpRequestException) { return LocalFailure<T>(execution, StructuredErrorKind.TransportFailure, true); }
        catch (IOException) { return LocalFailure<T>(execution, StructuredErrorKind.TransportFailure, true); }
        catch (Exception e) when (e is JsonException or ArgumentException or InvalidOperationException or OverflowException) { return LocalFailure<T>(execution, StructuredErrorKind.InvalidResponse); }
    }

    static StructuredErrorKind ClassifyStatus(int status) => status switch
    {
        401 => StructuredErrorKind.Authentication,
        403 => StructuredErrorKind.PermissionDenied,
        429 => StructuredErrorKind.RateLimited,
        408 or >= 500 and <= 599 => StructuredErrorKind.ProviderUnavailable,
        _ => StructuredErrorKind.ProviderRejected
    };

    async Task<JsonElement> ReadBatchEnvelope(HttpResponseMessage response, OpenAiExecution execution)
    {
        using var source = await execution.Await(response.Content.ReadAsStreamAsync(execution.Token), s => s.Dispose()).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        await CopyLimited(source, buffer, _options.MaxResponseBytes, execution).ConfigureAwait(false);
        return JsonSerializer.Deserialize<JsonElement>(buffer.ToArray());
    }

    static async Task CopyLimited(Stream source, Stream destination, long limit, OpenAiExecution execution)
    {
        var buffer = new byte[8192];
        long total = 0;
        int count;
        while ((count = await execution.Await(source.ReadAsync(buffer, execution.Token).AsTask()).ConfigureAwait(false)) != 0)
        {
            total += count;
            if (total > limit) throw new JsonException("Download limit exceeded.");
            await execution.Await(destination.WriteAsync(buffer.AsMemory(0, count), execution.Token).AsTask()).ConfigureAwait(false);
        }
    }

    static string RemoteId(string id) { if (String.IsNullOrWhiteSpace(id)) throw new ArgumentException("A remote ID is required."); return Uri.EscapeDataString(id); }
    static HttpContent JsonContent(object body) => new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
    static OpenAiFile ReadFile(JsonElement root) => new() { Id = Text(root, "id") ?? throw new JsonException(), Filename = Text(root, "filename") ?? throw new JsonException(), Bytes = Property(root, "bytes").GetInt64(), Purpose = Text(root, "purpose") };
    static OpenAiBatch ReadBatch(JsonElement root) => new() { Id = Text(root, "id") ?? throw new JsonException(), Status = Text(root, "status") ?? throw new JsonException(), InputFileId = Text(root, "input_file_id") ?? throw new JsonException(), Endpoint = Text(root, "endpoint") ?? throw new JsonException(), OutputFileId = Text(root, "output_file_id"), ErrorFileId = Text(root, "error_file_id") };

    /// <summary>Uploads a batch JSONL file as multipart form data; leaves the source stream open.</summary>
    public Task<StructuredResult<OpenAiFile>> UploadBatchFileAsync(Stream source, string filename = "batch.jsonl", BatchOperationOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        return BatchOperation(options, "UploadBatchFile", async execution =>
        {
            if (!source.CanRead || String.IsNullOrWhiteSpace(filename)) return InvalidOptions<OpenAiFile>(execution);
            using var buffer = new MemoryStream();
            try { await CopyLimited(source, buffer, 200L * 1024 * 1024, execution).ConfigureAwait(false); }
            catch (JsonException) { return InvalidOptions<OpenAiFile>(execution); }
            var multipart = new MultipartFormDataContent();
            multipart.Add(new StringContent("batch"), "purpose");
            multipart.Add(new ByteArrayContent(buffer.ToArray()), "file", filename);
            return await BatchHttp(HttpMethod.Post, "files", multipart, options, execution, async response => ReadFile(await ReadBatchEnvelope(response, execution).ConfigureAwait(false))).ConfigureAwait(false);
        }, cancellationToken);
    }

    /// <summary>Gets batch file metadata.</summary>
    public Task<StructuredResult<OpenAiFile>> GetFileAsync(string fileId, BatchOperationOptions? options = null, CancellationToken cancellationToken = default)
        => BatchOperation(options, "GetFile", execution => BatchHttp(HttpMethod.Get, "files/" + RemoteId(fileId), null, options, execution, async r => ReadFile(await ReadBatchEnvelope(r, execution).ConfigureAwait(false))), cancellationToken);

    /// <summary>Deletes a batch file explicitly; remote resources are never cleaned up implicitly.</summary>
    public Task<StructuredResult<bool>> DeleteFileAsync(string fileId, BatchOperationOptions? options = null, CancellationToken cancellationToken = default)
        => BatchOperation(options, "DeleteFile", execution => BatchHttp(HttpMethod.Delete, "files/" + RemoteId(fileId), null, options, execution, async r => Property(await ReadBatchEnvelope(r, execution).ConfigureAwait(false), "deleted").GetBoolean()), cancellationToken);

    /// <summary>Downloads file content within the configured limit and leaves the destination stream open.</summary>
    public Task<StructuredResult<bool>> DownloadFileContentAsync(string fileId, Stream destination, BatchOperationOptions? options = null, CancellationToken cancellationToken = default)
        => BatchOperation(options, "DownloadFile", execution => Download(fileId, destination, options, execution), cancellationToken);

    Task<StructuredResult<bool>> Download(string fileId, Stream destination, BatchOperationOptions? options, OpenAiExecution execution)
        => BatchHttp(HttpMethod.Get, "files/" + RemoteId(fileId) + "/content", null, options, execution, async response =>
        {
            using var source = await execution.Await(response.Content.ReadAsStreamAsync(execution.Token), s => s.Dispose()).ConfigureAwait(false);
            await CopyLimited(source, destination, options?.MaxDownloadBytes ?? 256L * 1024 * 1024, execution).ConfigureAwait(false);
            return true;
        });

    /// <summary>Creates one batch using a supported endpoint and the provider 24h completion window.</summary>
    public Task<StructuredResult<OpenAiBatch>> CreateBatchAsync(string inputFileId, string endpoint, BatchOperationOptions? options = null, CancellationToken cancellationToken = default)
        => BatchOperation(options, "CreateBatch", execution => CreateBatch(inputFileId, endpoint, options, execution), cancellationToken);

    Task<StructuredResult<OpenAiBatch>> CreateBatch(string inputFileId, string endpoint, BatchOperationOptions? options, OpenAiExecution execution)
    {
        if (String.IsNullOrWhiteSpace(inputFileId) || endpoint is not ("/v1/responses" or "/v1/embeddings")) return Task.FromResult(InvalidOptions<OpenAiBatch>(execution));
        return BatchHttp(HttpMethod.Post, "batches", JsonContent(new { input_file_id = inputFileId, endpoint, completion_window = "24h" }), options, execution, async r => ReadBatch(await ReadBatchEnvelope(r, execution).ConfigureAwait(false)));
    }

    /// <summary>Gets the current provider batch state without polling.</summary>
    public Task<StructuredResult<OpenAiBatch>> GetBatchAsync(string batchId, BatchOperationOptions? options = null, CancellationToken cancellationToken = default)
        => BatchOperation(options, "GetBatch", execution => GetBatch(batchId, options, execution), cancellationToken);
    Task<StructuredResult<OpenAiBatch>> GetBatch(string id, BatchOperationOptions? options, OpenAiExecution execution)
        => BatchHttp(HttpMethod.Get, "batches/" + RemoteId(id), null, options, execution, async r => ReadBatch(await ReadBatchEnvelope(r, execution).ConfigureAwait(false)));

    /// <summary>Requests batch cancellation; partial terminal results remain importable.</summary>
    public Task<StructuredResult<OpenAiBatch>> CancelBatchAsync(string batchId, BatchOperationOptions? options = null, CancellationToken cancellationToken = default)
        => BatchOperation(options, "CancelBatch", execution => BatchHttp(HttpMethod.Post, "batches/" + RemoteId(batchId) + "/cancel", null, options, execution, async r => ReadBatch(await ReadBatchEnvelope(r, execution).ConfigureAwait(false))), cancellationToken);

    /// <summary>Lists one page of batches, with an optional cursor.</summary>
    public Task<StructuredResult<OpenAiBatchPage>> ListBatchesAsync(string? after = null, int limit = 20, BatchOperationOptions? options = null, CancellationToken cancellationToken = default)
        => BatchOperation(options, "ListBatches", execution => limit is < 1 or > 100 ? Task.FromResult(InvalidOptions<OpenAiBatchPage>(execution)) : BatchHttp(HttpMethod.Get, "batches?limit=" + limit + (after is null ? "" : "&after=" + RemoteId(after)), null, options, execution, async r =>
        {
            var root = await ReadBatchEnvelope(r, execution).ConfigureAwait(false);
            return new OpenAiBatchPage { Data = Array.AsReadOnly(Property(root, "data").EnumerateArray().Select(ReadBatch).ToArray()), HasMore = Property(root, "has_more").GetBoolean(), LastId = Text(root, "last_id") };
        }), cancellationToken);

    /// <summary>Validates and snapshots one-model embedding inputs into JSONL and a versioned manifest.</summary>
    public PreparedBatch PrepareEmbeddingBatch(IEnumerable<EmbeddingBatchItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        using var lines = new MemoryStream();
        var manifest = new List<BatchManifestItem>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var inputs = 0;
        foreach (var item in items)
        {
            var request = item.Request;
            if (request.CredentialResolver is not null || request.TotalTimeout is not null || request.UsageObserver is not null || request.IdempotencyKey is not null) throw new ArgumentException("Use operation controls for batch transport.");
            var texts = request.Inputs.ToArray();
            if (texts.Length is < 1 or > 2048 || texts.Any(String.IsNullOrWhiteSpace) || request.Dimensions is <= 0) throw new ArgumentException("Invalid embedding inputs.");
            inputs = checked(inputs + texts.Length);
            if (inputs > 50000) throw new ArgumentException("Embedding batches support at most 50,000 inputs.");
            var model = SelectModel(request.ModelSelection, _options.EmbeddingProfiles);
            if (model.ReasoningEffort is not null) throw new ArgumentException("Embedding reasoning is unsupported.");
            var body = new Dictionary<string, object?> { ["model"] = model.ModelId, ["input"] = texts, ["encoding_format"] = "float" };
            if (request.Dimensions is { } dimensions) body["dimensions"] = dimensions;
            CheckPreparedItem(manifest, ids, item.CustomId, model.ModelId!);
            WriteBatchLine(lines, new { custom_id = item.CustomId, method = "POST", url = "/v1/embeddings", body });
            manifest.Add(new() { CustomId = item.CustomId, Position = manifest.Count, ExecutionId = Guid.NewGuid(), CorrelationId = request.CorrelationId, RequestedModel = model.ModelId!, InputCount = texts.Length, Dimensions = request.Dimensions, CaptureRawResponse = request.CaptureRawResponse });
        }
        return Prepare(lines, manifest, "/v1/embeddings");
    }

    /// <summary>Validates and snapshots homogeneous typed Responses inputs, including vocabulary and schema fingerprints.</summary>
    public PreparedBatch PrepareResponseBatch<T>(StructuredTask<T> task, IEnumerable<ResponseBatchItem> items)
    {
        ArgumentNullException.ThrowIfNull(task); ArgumentNullException.ThrowIfNull(items);
        using var lines = new MemoryStream(); var manifest = new List<BatchManifestItem>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            var request = item.Request;
            ValidateResponseRequest(request);
            if (request.Stream || request.Progress is not null || request.CredentialResolver is not null || request.TotalTimeout is not null || request.UsageObserver is not null || request.OpenAi.IdempotencyKey is not null) throw new ArgumentException("Batch items cannot specify streaming or transport controls.");
            var instructions = request.Instructions ?? task.Instructions;
            if (String.IsNullOrWhiteSpace(instructions)) throw new ArgumentException("Typed execution requires instructions.");
            var vocabularies = request.Vocabularies?.ToDictionary(x => x.Key, x => x.Value.ToArray(), StringComparer.Ordinal);
            var values = vocabularies?.ToDictionary(x => x.Key, x => (IReadOnlyList<string>)x.Value);
            var schema = task.CreateSchema(values);
            var model = SelectModel(request.ModelSelection ?? task.ModelSelection ?? _options.DefaultModel);
            var body = ResponsePayload(request, model, instructions, request.OutputSpecification is { } specification ? task.CreateOutputSpecification(specification, values) : null);
            var format = new Dictionary<string, object?> { ["type"] = "json_schema", ["name"] = task.SchemaName, ["schema"] = schema, ["strict"] = true };
            if (task.Description is not null) format["description"] = task.Description;
            body["text"] = new { format };
            CheckPreparedItem(manifest, ids, item.CustomId, model.ModelId!);
            WriteBatchLine(lines, new { custom_id = item.CustomId, method = "POST", url = "/v1/responses", body });
            manifest.Add(new() { CustomId = item.CustomId, Position = manifest.Count, ExecutionId = Guid.NewGuid(), CorrelationId = request.CorrelationId, RequestedModel = model.ModelId!, Vocabularies = vocabularies, SchemaFingerprint = Fingerprint(schema), CaptureRawResponse = request.OpenAi.CaptureRawResponse, CaptureOutputText = request.OpenAi.CaptureOutputText });
        }
        return Prepare(lines, manifest, "/v1/responses");
    }

    static string Fingerprint(JsonElement schema) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(schema.GetRawText())));
    static void CheckPreparedItem(List<BatchManifestItem> items, HashSet<string> ids, string id, string model)
    {
        if (items.Count >= 50000 || String.IsNullOrWhiteSpace(id) || id.Length > 64 || !ids.Add(id))
            throw new ArgumentException("Batch custom IDs must be nonblank, unique and at most 64 characters; jobs support at most 50,000 requests.");
        if (items.Count > 0 && items[0].RequestedModel != model) throw new ArgumentException("A batch must use one resolved model.");
    }

    static void WriteBatchLine(MemoryStream destination, object line)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(line);
        if (destination.Length + bytes.Length + 1 > 200L * 1024 * 1024) throw new ArgumentException("Batch files support at most 200 MiB.");
        destination.Write(bytes);
        destination.WriteByte(10);
    }

    static PreparedBatch Prepare(MemoryStream lines, List<BatchManifestItem> items, string endpoint)
    {
        ValidateManifest(new() { Endpoint = endpoint, Items = items }, endpoint);
        return new(lines.ToArray(), new() { Endpoint = endpoint, Items = items.AsReadOnly() });
    }

    static void ValidateManifest(BatchManifest manifest, string endpoint)
    {
        if (manifest.Version != 1 || manifest.Endpoint != endpoint || manifest.Items is null || manifest.Items.Count is < 1 or > 50000) throw new ArgumentException("Invalid manifest version, endpoint or item count.");
        if (endpoint == "/v1/embeddings" && manifest.Items.Sum(x => (long)x.InputCount) > 50000) throw new ArgumentException("Embedding batches support at most 50,000 inputs.");
        var ids = new HashSet<string>(StringComparer.Ordinal); var executions = new HashSet<Guid>(); string? model = null;
        for (var i = 0; i < manifest.Items.Count; i++)
        {
            var item = manifest.Items[i];
            if (item.Position != i || String.IsNullOrWhiteSpace(item.CustomId) || item.CustomId.Length > 64 || !ids.Add(item.CustomId) || item.ExecutionId == Guid.Empty || !executions.Add(item.ExecutionId) || String.IsNullOrWhiteSpace(item.RequestedModel)) throw new ArgumentException("Invalid batch item identity.");
            model ??= item.RequestedModel;
            if (model != item.RequestedModel) throw new ArgumentException("A batch must use one resolved model.");
            if (endpoint == "/v1/embeddings" && (item.InputCount is < 1 or > 2048 || item.Dimensions is <= 0)) throw new ArgumentException("Invalid embedding manifest.");
        }
    }

    /// <summary>Uploads prepared JSONL and creates a batch; retains UploadedFileId on creation failure without retry or cleanup.</summary>
    public Task<StructuredResult<OpenAiBatch>> SubmitBatchAsync(PreparedBatch batch, BatchOperationOptions? options = null, CancellationToken cancellationToken = default)
        => BatchOperation(options, "SubmitBatch", async execution =>
        {
            try { ValidateManifest(batch.Manifest, batch.Manifest.Endpoint); }
            catch (ArgumentException) { return InvalidOptions<OpenAiBatch>(execution); }
            var multipart = new MultipartFormDataContent(); multipart.Add(new StringContent("batch"), "purpose"); multipart.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(batch.Jsonl)), "file", "batch.jsonl");
            var uploaded = await BatchHttp(HttpMethod.Post, "files", multipart, options, execution, async r => ReadFile(await ReadBatchEnvelope(r, execution).ConfigureAwait(false))).ConfigureAwait(false);
            if (!uploaded.IsSuccess) return StructuredResult<OpenAiBatch>.Failure(uploaded.Error!, execution.Metadata);
            execution.Metadata = execution.Metadata with { UploadedFileId = uploaded.Value!.Id };
            return await CreateBatch(uploaded.Value.Id, batch.Manifest.Endpoint, options, execution).ConfigureAwait(false);
        }, cancellationToken);

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
            if (Fingerprint(task.CreateSchema(item.Vocabularies?.ToDictionary(x => x.Key, x => (IReadOnlyList<string>)x.Value))) != item.SchemaFingerprint) throw new ArgumentException("Batch schema contract mismatch.");
        }, cancellationToken, task.SchemaName);

    Task<StructuredResult<IReadOnlyList<StructuredResult<T>>>> ImportBatch<T>(string batchId, BatchManifest manifest, string endpoint, BatchOperationOptions? options,
        Func<BatchManifestItem, JsonElement, OpenAiExecution, ValueTask<StructuredResult<T>>> process, Action<BatchManifestItem>? validate, CancellationToken caller, string? schemaName = null)
        => BatchOperation(options, "ImportBatch", async execution =>
        {
            try { manifest = BatchManifest.FromJson(manifest.ToJson()); ValidateManifest(manifest, endpoint); foreach (var item in manifest.Items) validate?.Invoke(item); }
            catch (ArgumentException) { return InvalidOptions<IReadOnlyList<StructuredResult<T>>>(execution); }
            execution.Metadata = execution.Metadata with { BatchId = batchId, SchemaName = schemaName };
            var job = await GetBatch(batchId, options, execution).ConfigureAwait(false);
            if (!job.IsSuccess) return StructuredResult<IReadOnlyList<StructuredResult<T>>>.Failure(job.Error!, execution.Metadata);
            if (!job.Value!.IsTerminal || job.Value.Endpoint != endpoint) return InvalidOptions<IReadOnlyList<StructuredResult<T>>>(execution);
            var index = manifest.Items.ToDictionary(x => x.CustomId, StringComparer.Ordinal);
            var results = new Dictionary<string, StructuredResult<T>>(StringComparer.Ordinal);
            long downloaded = 0;
            foreach (var file in new[] { job.Value.OutputFileId, job.Value.ErrorFileId }.Where(x => x is not null))
            {
                var imported = await BatchHttp(HttpMethod.Get, "files/" + RemoteId(file!) + "/content", null, options, execution, async response =>
                {
                    using var source = await execution.Await(response.Content.ReadAsStreamAsync(execution.Token), s => s.Dispose()).ConfigureAwait(false);
                    var chunk = new byte[8192]; using var line = new MemoryStream(); int count;
                    while ((count = await execution.Await(source.ReadAsync(chunk, execution.Token).AsTask()).ConfigureAwait(false)) != 0)
                    {
                        downloaded += count;
                        if (downloaded > (options?.MaxDownloadBytes ?? 256L * 1024 * 1024)) throw new JsonException();
                        for (var i = 0; i < count; i++)
                        {
                            if (chunk[i] == 10) { if (line.Length > 0) await ReadLine().ConfigureAwait(false); line.SetLength(0); }
                            else { if (line.Length >= _options.MaxResponseBytes) throw new JsonException(); line.WriteByte(chunk[i]); }
                        }
                    }
                    if (line.Length > 0) await ReadLine().ConfigureAwait(false);
                    return true;
                    async Task ReadLine()
                    {
                        using var document = JsonDocument.Parse(line.ToArray()); var root = document.RootElement;
                        var id = Text(root, "custom_id");
                        if (id is null || !index.TryGetValue(id, out var item) || results.ContainsKey(id)) throw new JsonException();
                        using var itemExecution = new OpenAiExecution(BatchControls(options), _options, caller);
                        var envelope = Property(root, "response"); var body = Property(envelope, "body");
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
                            RawResponse = item.CaptureRawResponse && body.ValueKind != JsonValueKind.Undefined ? body : null
                        };
                        StructuredResult<T> result;
                        var status = Property(envelope, "status_code");
                        if (Property(root, "error").ValueKind == JsonValueKind.Object) result = LocalFailure<T>(itemExecution, StructuredErrorKind.BatchItemFailed);
                        else if (status.ValueKind != JsonValueKind.Number || !status.TryGetInt32(out var code) || code is < 100 or > 599) throw new JsonException();
                        else if (code is < 200 or >= 300) result = StructuredResult<T>.Failure(new() { Kind = ClassifyStatus(code), Message = "Batch item HTTP failure.", HttpStatusCode = (HttpStatusCode)code, IsTransient = ClassifyStatus(code) == StructuredErrorKind.ProviderUnavailable || ClassifyStatus(code) == StructuredErrorKind.RateLimited && Text(Property(body, "error"), "code") != "insufficient_quota" }, itemExecution.Metadata);
                        else result = await process(item, body, itemExecution).ConfigureAwait(false);
                        if (itemExecution.Metadata.Usage is { } usage && (options?.UsageObserver ?? _options.UsageObserver) is { } observer)
                            await execution.Callback(token => observer(new() { Provider = "openai", RequestedModel = item.RequestedModel, Usage = usage, Metadata = itemExecution.Metadata, Succeeded = result.IsSuccess, FailureKind = result.Error?.Kind, CallerCancelled = caller.IsCancellationRequested }, token), TimeSpan.FromSeconds(5), "UsageObserver").ConfigureAwait(false);
                        results.Add(id, result.IsSuccess ? StructuredResult<T>.Success(result.Value!, itemExecution.Metadata, itemExecution.Warnings) : StructuredResult<T>.Failure(result.Error!, itemExecution.Metadata, itemExecution.Warnings));
                    }
                }).ConfigureAwait(false);
                if (!imported.IsSuccess) return StructuredResult<IReadOnlyList<StructuredResult<T>>>.Failure(imported.Error!, execution.Metadata, execution.Warnings);
            }
            var ordered = manifest.Items.Select(item => results.TryGetValue(item.CustomId, out var result) ? result : StructuredResult<T>.Failure(new() { Kind = StructuredErrorKind.IncompleteOutput, Message = "Terminal batch has no result for this item.", Issues = [new("$", "BatchResultMissing", "The terminal batch omitted this item.")] }, new() { ExecutionId = item.ExecutionId, SchemaName = schemaName, Provider = "openai", RequestedModel = item.RequestedModel, CorrelationId = item.CorrelationId, BatchId = batchId, BatchCustomId = item.CustomId, IsBatch = true })).ToArray();
            return StructuredResult<IReadOnlyList<StructuredResult<T>>>.Success(Array.AsReadOnly(ordered), execution.Metadata, execution.Warnings);
        }, caller);
}
