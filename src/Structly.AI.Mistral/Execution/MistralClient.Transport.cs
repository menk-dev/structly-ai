using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Structly.AI.Mistral;

public sealed partial class MistralClient
{
    async Task<StructuredResult<T>> RunOperation<T>(StructuredRequest request, string operation,
        Func<MistralExecution, Task<StructuredResult<T>>> run, CancellationToken caller, string? schemaName = null)
    {
        var metadata = new StructuredMetadata { Provider = "Mistral", Operation = operation, SchemaName = schemaName, CorrelationId = request.CorrelationId };
        if(caller.IsCancellationRequested)
            throw new StructuredOperationCanceledException(metadata, [], caller);

        var timeout = request.TotalTimeout ?? _totalTimeout;
        using var execution = new MistralExecution(request, _clock, _totalTimeout, caller) { Metadata = metadata };
        if(!ValidTimeout(timeout))
            return execution.Failure<T>(StructuredErrorKind.InvalidRequest);

        try
        {
            var result = await run(execution).ConfigureAwait(false);
            execution.CheckCancellation();
            var observer = request.UsageObserver ?? _usageObserver;
            if(observer is not null && result.Metadata.Usage is { } usage)
            {
                await execution.Callback(token => observer(new()
                {
                    Provider = "Mistral",
                    RequestedModel = result.Metadata.RequestedModel!,
                    Usage = usage,
                    Metadata = result.Metadata,
                    Succeeded = result.IsSuccess,
                    FailureKind = result.Error?.Kind,
                }, token), TimeSpan.FromSeconds(5), "UsageObserver").ConfigureAwait(false);
            }

            execution.CheckCancellation();
            return result.IsSuccess ? StructuredResult<T>.Success(result.Value!, execution.Metadata, execution.Warnings)
                : StructuredResult<T>.Failure(result.Error!, execution.Metadata, execution.Warnings);
        }
        catch(OperationCanceledException)
        {
            if(caller.IsCancellationRequested)
                throw new StructuredOperationCanceledException(execution.Metadata, execution.Warnings, caller);

            return execution.Failure<T>(execution.Deadline ?? StructuredErrorKind.TransportFailure);
        }
        catch(StructuredSchemaException)
        {
            return execution.Failure<T>(StructuredErrorKind.UnsupportedSchema);
        }
        catch(ArgumentException)
        {
            return execution.Failure<T>(StructuredErrorKind.InvalidRequest);
        }
        catch(HttpRequestException)
        {
            return execution.Failure<T>(StructuredErrorKind.TransportFailure);
        }
        catch(IOException)
        {
            return execution.Failure<T>(StructuredErrorKind.TransportFailure);
        }
        catch(Exception exception) when(exception is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
        {
            return execution.Failure<T>(StructuredErrorKind.InvalidResponse);
        }
    }

    async Task<StructuredResult<T>> Send<T>(MistralExecution execution, StructuredRequest request,
        Func<CancellationToken, ValueTask<string?>>? taskCredentials, HttpMethod method, string path, HttpContent? content,
        Func<HttpResponseMessage, Task<StructuredResult<T>>> read)
    {
        using var message = new HttpRequestMessage(method, new Uri(_baseAddress, path)) { Content = content };
        string? key;
        try
        {
            var resolver = request.CredentialResolver ?? taskCredentials ?? _credentials;
            key = resolver is null ? null : await execution.Await(resolver(execution.Token).AsTask()).ConfigureAwait(false);
            if(String.IsNullOrWhiteSpace(key))
                return execution.Failure<T>(StructuredErrorKind.CredentialsMissing);

            if(key.Any(Char.IsWhiteSpace) || key.Any(Char.IsControl))
                return execution.Failure<T>(StructuredErrorKind.Authentication);
        }
        catch(Exception exception) when(exception is not OperationCanceledException and not OutOfMemoryException)
        {
            return execution.Failure<T>(StructuredErrorKind.Authentication);
        }

        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        using var response = await execution.Await(_http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, execution.Token), response => response.Dispose()).ConfigureAwait(false);
        execution.Metadata = execution.Metadata with { ProviderRequestId = response.Headers.TryGetValues("x-request-id", out var ids) ? ids.FirstOrDefault() : null };
        if(!response.IsSuccessStatusCode)
        {
            var delay = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date is { } date ? date - _clock.GetUtcNow() : (TimeSpan?)null);
            return execution.Failure<T>(ClassifyStatus(response.StatusCode), response.StatusCode, delay < TimeSpan.Zero ? TimeSpan.Zero : delay);
        }

        try
        {
            return await read(response).ConfigureAwait(false);
        }
        catch(Exception exception) when(exception is ArgumentException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
        {
            return execution.Failure<T>(StructuredErrorKind.InvalidResponse);
        }
    }

    static StructuredErrorKind ClassifyStatus(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized => StructuredErrorKind.Authentication,
        HttpStatusCode.Forbidden => StructuredErrorKind.PermissionDenied,
        HttpStatusCode.TooManyRequests => StructuredErrorKind.RateLimited,
        _ => (int)status >= 500 ? StructuredErrorKind.ProviderUnavailable : StructuredErrorKind.ProviderRejected,
    };

    async Task<JsonDocument> ReadEnvelope(HttpResponseMessage response, MistralExecution execution)
    {
        await using var source = await execution.Await(response.Content.ReadAsStreamAsync(execution.Token), source => source.Dispose()).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        await CopyLimited(source, buffer, _maxResponseBytes, execution).ConfigureAwait(false);
        return JsonDocument.Parse(buffer.ToArray());
    }

    static async Task CopyLimited(Stream source, Stream destination, long maximum, MistralExecution execution)
    {
        var bytes = new byte[8192];
        long total = 0;
        int count;
        while((count = await execution.Await(source.ReadAsync(bytes, execution.Token).AsTask()).ConfigureAwait(false)) != 0)
        {
            total += count;
            if(total > maximum)
                throw new JsonException();

            await execution.Await(destination.WriteAsync(bytes.AsMemory(0, count), execution.Token).AsTask()).ConfigureAwait(false);
        }
    }

    static void RetainEnvelope(JsonElement root, StructuredRequest request, MistralExecution execution)
        => execution.Metadata = execution.Metadata with
        {
            ResponseId = StringValue(root, "id"),
            ResolvedModel = StringValue(root, "model"),
            Usage = ReadUsage(root, execution.Warnings),
            RawResponse = request is MistralRequest { CaptureRawResponse: true } ? root : null,
        };
}
