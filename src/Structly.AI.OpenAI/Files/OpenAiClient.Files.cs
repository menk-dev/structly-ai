using System.Text.Json;

namespace Structly.AI.OpenAI;

public sealed partial class OpenAiClient
{
    static OpenAiFile ReadFile(JsonElement root) => new() { Id = Text(root, "id") ?? throw new JsonException(), Filename = Text(root, "filename") ?? throw new JsonException(), Bytes = Property(root, "bytes").GetInt64(), Purpose = Text(root, "purpose") };

    /// <summary>Uploads a batch JSONL file as multipart form data; leaves the source stream open.</summary>
    public Task<StructuredResult<OpenAiFile>> UploadBatchFileAsync(Stream source, string filename = "batch.jsonl", BatchOperationOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        return BatchOperation(options, "UploadBatchFile", async execution =>
        {
            if(!source.CanRead || String.IsNullOrWhiteSpace(filename))
                return InvalidOptions<OpenAiFile>(execution);

            using var buffer = new MemoryStream();
            try
            {
                await CopyLimited(source, buffer, 200L * 1024 * 1024, execution).ConfigureAwait(false);
            }
            catch(JsonException)
            {
                return InvalidOptions<OpenAiFile>(execution);
            }

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

}
