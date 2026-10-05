using System.Text;
using System.Text.Json;

namespace Structly.AI.OpenAI;

public sealed partial class OpenAiClient
{
    static OpenAiBatch ReadBatch(JsonElement root) => new() { Id = Text(root, "id") ?? throw new JsonException(), Status = Text(root, "status") ?? throw new JsonException(), InputFileId = Text(root, "input_file_id") ?? throw new JsonException(), Endpoint = Text(root, "endpoint") ?? throw new JsonException(), OutputFileId = Text(root, "output_file_id"), ErrorFileId = Text(root, "error_file_id") };

    /// <summary>Creates one batch using a supported endpoint and the provider 24h completion window.</summary>
    public Task<StructuredResult<OpenAiBatch>> CreateBatchAsync(string inputFileId, string endpoint, BatchOperationOptions? options = null, CancellationToken cancellationToken = default)
        => BatchOperation(options, "CreateBatch", execution => CreateBatch(inputFileId, endpoint, options, execution), cancellationToken);

    Task<StructuredResult<OpenAiBatch>> CreateBatch(string inputFileId, string endpoint, BatchOperationOptions? options, OpenAiExecution execution)
    {
        if(String.IsNullOrWhiteSpace(inputFileId) || endpoint is not ("/v1/responses" or "/v1/embeddings"))
            return Task.FromResult(InvalidOptions<OpenAiBatch>(execution));

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

    /// <summary>Uploads prepared JSONL and creates a batch; retains UploadedFileId on creation failure without retry or cleanup.</summary>
    public Task<StructuredResult<OpenAiBatch>> SubmitBatchAsync(PreparedBatch batch, BatchOperationOptions? options = null, CancellationToken cancellationToken = default)
        => BatchOperation(options, "SubmitBatch", async execution =>
        {
            try
            {
                ValidateManifest(batch.Manifest, batch.Manifest.Endpoint);
            }
            catch(ArgumentException)
            {
                return InvalidOptions<OpenAiBatch>(execution);
            }

            var multipart = new MultipartFormDataContent();
            multipart.Add(new StringContent("batch"), "purpose");
            multipart.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(batch.Jsonl)), "file", "batch.jsonl");
            var uploaded = await BatchHttp(HttpMethod.Post, "files", multipart, options, execution, async r => ReadFile(await ReadBatchEnvelope(r, execution).ConfigureAwait(false))).ConfigureAwait(false);
            if(!uploaded.IsSuccess)
                return StructuredResult<OpenAiBatch>.Failure(uploaded.Error!, execution.Metadata);

            execution.Metadata = execution.Metadata with { UploadedFileId = uploaded.Value!.Id };
            return await CreateBatch(uploaded.Value.Id, batch.Manifest.Endpoint, options, execution).ConfigureAwait(false);
        }, cancellationToken);

}
