using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Structly.AI.Mistral;

public sealed partial class MistralClient
{
    static MistralBatch ReadBatch(JsonElement root) => new()
    {
        Id = StringValue(root, "id") ?? throw new JsonException(),
        Status = StringValue(root, "status") ?? throw new JsonException(),
        InputFileIds = Array.AsReadOnly(root.GetProperty("input_files").EnumerateArray().Select(x => x.GetString() ?? throw new JsonException()).ToArray()),
        Endpoint = StringValue(root, "endpoint") ?? throw new JsonException(),
        Model = StringValue(root, "model"),
        OutputFileId = StringValue(root, "output_file"),
        ErrorFileId = StringValue(root, "error_file"),
    };

    /// <summary>Creates a one-model chat or embedding job from an uploaded batch file.</summary>
    public Task<StructuredResult<MistralBatch>> CreateBatchAsync(string inputFileId, string endpoint, ModelSelection model,
        BatchOperationOptions? options = null, CancellationToken cancellationToken = default)
        => BatchOperation(options, "CreateBatch", execution => CreateBatch(inputFileId, endpoint, model, options, execution), cancellationToken);

    Task<StructuredResult<MistralBatch>> CreateBatch(string fileId, string endpoint, ModelSelection model, BatchOperationOptions? options, MistralExecution execution)
    {
        RemoteId(fileId);
        if(endpoint is not ("/v1/chat/completions" or "/v1/embeddings") || model is null)
            throw new ArgumentException("Unsupported batch endpoint/model.");

        var selection = SelectModel(model);
        if(selection.ReasoningEffort is not null)
            throw new ArgumentException("Specify reasoning in individual chat bodies.");

        return BatchHttp(HttpMethod.Post, "batch/jobs", JsonContent.Create(new { input_files = new[] { fileId }, endpoint, model = selection.ModelId }),
            options, execution, async response => ReadBatch(await ReadBatchEnvelope(response, execution).ConfigureAwait(false)));
    }

    /// <summary>Gets the current job state without polling.</summary>
    public Task<StructuredResult<MistralBatch>> GetBatchAsync(string batchId, BatchOperationOptions? options = null, CancellationToken cancellationToken = default)
        => BatchOperation(options, "GetBatch", execution => GetBatch(batchId, options, execution), cancellationToken);

    Task<StructuredResult<MistralBatch>> GetBatch(string id, BatchOperationOptions? options, MistralExecution execution)
        => BatchHttp(HttpMethod.Get, "batch/jobs/" + RemoteId(id), null, options, execution,
            async response => ReadBatch(await ReadBatchEnvelope(response, execution).ConfigureAwait(false)));

    /// <summary>Requests cancellation; partial results remain available when the job becomes terminal.</summary>
    public Task<StructuredResult<MistralBatch>> CancelBatchAsync(string batchId, BatchOperationOptions? options = null, CancellationToken cancellationToken = default)
        => BatchOperation(options, "CancelBatch", execution => BatchHttp(HttpMethod.Post, "batch/jobs/" + RemoteId(batchId) + "/cancel", null, options, execution,
            async response => ReadBatch(await ReadBatchEnvelope(response, execution).ConfigureAwait(false))), cancellationToken);

    /// <summary>Lists a numbered page of jobs using Mistral pagination.</summary>
    public Task<StructuredResult<MistralBatchPage>> ListBatchesAsync(int page = 0, int pageSize = 100, BatchOperationOptions? options = null, CancellationToken cancellationToken = default)
        => BatchOperation(options, "ListBatches", execution =>
        {
            if(page < 0 || pageSize is < 1 or > 100)
                throw new ArgumentException("Invalid pagination.");

            return BatchHttp(HttpMethod.Get, $"batch/jobs?page={page}&page_size={pageSize}", null, options, execution, async response =>
            {
                var root = await ReadBatchEnvelope(response, execution).ConfigureAwait(false);
                return new MistralBatchPage { Data = Array.AsReadOnly(root.GetProperty("data").EnumerateArray().Select(ReadBatch).ToArray()), Total = root.GetProperty("total").GetInt64() };
            });
        }, cancellationToken);

    /// <summary>Uploads prepared JSONL and creates a job, retaining the uploaded file ID on creation failure.</summary>
    public Task<StructuredResult<MistralBatch>> SubmitBatchAsync(PreparedBatch batch, BatchOperationOptions? options = null, CancellationToken cancellationToken = default)
        => BatchOperation(options, "SubmitBatch", async execution =>
        {
            ArgumentNullException.ThrowIfNull(batch);
            var manifest = batch.Manifest;
            ValidateManifest(manifest, manifest.Endpoint);
            var multipart = new MultipartFormDataContent();
            multipart.Add(new StringContent("batch"), "purpose");
            multipart.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(batch.Jsonl)), "file", "batch.jsonl");
            var uploaded = await BatchHttp(HttpMethod.Post, "files", multipart, options, execution,
                async response => ReadFile(await ReadBatchEnvelope(response, execution).ConfigureAwait(false))).ConfigureAwait(false);
            if(!uploaded.IsSuccess)
                return StructuredResult<MistralBatch>.Failure(uploaded.Error!, execution.Metadata);

            execution.Metadata = execution.Metadata with { UploadedFileId = uploaded.Value!.Id };
            return await CreateBatch(uploaded.Value.Id, manifest.Endpoint, new() { ModelId = manifest.Items[0].RequestedModel }, options, execution).ConfigureAwait(false);
        }, cancellationToken);
}
