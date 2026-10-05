using System.Text.Json;

namespace Structly.AI.Mistral;

public sealed partial class MistralClient
{
    static StructuredRequest BatchControls(BatchOperationOptions? options) => new()
    {
        TotalTimeout = options?.TotalTimeout,
        CredentialResolver = options?.CredentialResolver,
        UsageObserver = options?.UsageObserver,
        CorrelationId = options?.CorrelationId,
    };

    Task<StructuredResult<T>> BatchOperation<T>(BatchOperationOptions? options, string operation,
        Func<MistralExecution, Task<StructuredResult<T>>> run, CancellationToken caller)
        => RunOperation(BatchControls(options), operation, execution =>
        {
            if(options?.MaxDownloadBytes is <= 0)
                throw new ArgumentException("Invalid download limit.");

            return run(execution);
        }, caller);

    Task<StructuredResult<T>> BatchHttp<T>(HttpMethod method, string path, HttpContent? content,
        BatchOperationOptions? options, MistralExecution execution, Func<HttpResponseMessage, Task<T>> read)
        => Send(execution, BatchControls(options), null, method, path, content,
            async response => StructuredResult<T>.Success(await read(response).ConfigureAwait(false), execution.Metadata));

    async Task<JsonElement> ReadBatchEnvelope(HttpResponseMessage response, MistralExecution execution)
    {
        using var document = await ReadEnvelope(response, execution).ConfigureAwait(false);
        return document.RootElement.Clone();
    }

    static string RemoteId(string id)
    {
        if(String.IsNullOrWhiteSpace(id) || id.Any(c => !Char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
            throw new ArgumentException("Invalid resource ID.");

        return id;
    }
}
