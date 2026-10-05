using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Structly.AI.OpenAI;

public sealed partial class OpenAiClient
{
    static StructuredRequest BatchControls(BatchOperationOptions? options) => new()
    {
        TotalTimeout = options?.TotalTimeout,
        CredentialResolver = options?.CredentialResolver,
        CorrelationId = options?.CorrelationId,
        OpenAi = new() { IdempotencyKey = options?.IdempotencyKey },
    };

    Task<StructuredResult<T>> BatchOperation<T>(BatchOperationOptions? options, string name,
        Func<OpenAiExecution, Task<StructuredResult<T>>> action, CancellationToken token)
        => RunOperation(BatchControls(options), name, async execution =>
        {
            execution.CheckCancellation();
            try
            {
                ValidateCommonRequest(BatchControls(options));
                if(options?.MaxDownloadBytes <= 0)
                    throw new ArgumentException();
            }
            catch(ArgumentException)
            {
                return InvalidOptions<T>(execution);
            }

            try
            {
                return await action(execution).ConfigureAwait(false);
            }
            catch(ArgumentException)
            {
                return InvalidOptions<T>(execution);
            }
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
        catch(OperationCanceledException) when(execution.Token.IsCancellationRequested)
        {
            throw;
        }
        catch(Exception e) when(e is not OutOfMemoryException and not StackOverflowException)
        {
            return LocalFailure<T>(execution, StructuredErrorKind.Authentication);
        }

        if(String.IsNullOrWhiteSpace(credential))
            return LocalFailure<T>(execution, StructuredErrorKind.CredentialsMissing);

        if(credential.Any(c => c < 33 || c > 126))
            return LocalFailure<T>(execution, StructuredErrorKind.Authentication);

        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);
        if(options?.IdempotencyKey is { } key && method == HttpMethod.Post)
            message.Headers.Add("Idempotency-Key", key);

        try
        {
            execution.CheckCancellation();
            using var response = await execution.Await(_http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, execution.Token), r => r.Dispose()).ConfigureAwait(false);
            execution.Metadata = execution.Metadata with { ProviderRequestId = response.Headers.TryGetValues("x-request-id", out var ids) ? ids.FirstOrDefault() : null };
            if(!response.IsSuccessStatusCode)
            {
                var kind = ClassifyStatus((int)response.StatusCode);
                string? providerCode = null;
                try
                {
                    providerCode = Text(Property(await ReadBatchEnvelope(response, execution).ConfigureAwait(false), "error"), "code");
                }
                catch(Exception e) when(e is JsonException or ArgumentException or InvalidOperationException) { }

                var delay = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date is { } date ? date - _options.TimeProvider.GetUtcNow() : (TimeSpan?)null);
                return StructuredResult<T>.Failure(new()
                {
                    Kind = kind,
                    Message = $"Batch operation failed: {kind}.",
                    HttpStatusCode = response.StatusCode,
                    IsTransient = kind == StructuredErrorKind.ProviderUnavailable || kind == StructuredErrorKind.RateLimited && providerCode != "insufficient_quota",
                    RetryAfter = delay < TimeSpan.Zero ? TimeSpan.Zero : delay,
                }, execution.Metadata);
            }

            var value = await read(response).ConfigureAwait(false);
            execution.CheckCancellation();
            return StructuredResult<T>.Success(value, execution.Metadata);
        }
        catch(OperationCanceledException) when(!execution.Token.IsCancellationRequested)
        {
            return LocalFailure<T>(execution, StructuredErrorKind.TransportFailure, true);
        }
        catch(HttpRequestException)
        {
            return LocalFailure<T>(execution, StructuredErrorKind.TransportFailure, true);
        }
        catch(IOException)
        {
            return LocalFailure<T>(execution, StructuredErrorKind.TransportFailure, true);
        }
        catch(Exception e) when(e is JsonException or ArgumentException or InvalidOperationException or OverflowException)
        {
            return LocalFailure<T>(execution, StructuredErrorKind.InvalidResponse);
        }
    }

    static StructuredErrorKind ClassifyStatus(int status) => status switch
    {
        401 => StructuredErrorKind.Authentication,
        403 => StructuredErrorKind.PermissionDenied,
        429 => StructuredErrorKind.RateLimited,
        408 or >= 500 and <= 599 => StructuredErrorKind.ProviderUnavailable,
        _ => StructuredErrorKind.ProviderRejected,
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
        while((count = await execution.Await(source.ReadAsync(buffer, execution.Token).AsTask()).ConfigureAwait(false)) != 0)
        {
            total += count;
            if(total > limit)
                throw new JsonException("Download limit exceeded.");

            await execution.Await(destination.WriteAsync(buffer.AsMemory(0, count), execution.Token).AsTask()).ConfigureAwait(false);
        }
    }

    static string RemoteId(string id)
    {
        if(String.IsNullOrWhiteSpace(id))
            throw new ArgumentException("A remote ID is required.");

        return Uri.EscapeDataString(id);
    }
    static HttpContent JsonContent(object body) => new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

}
