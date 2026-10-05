using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Structly.AI.OpenAI;

public sealed partial class OpenAiClient
{
    async Task<StructuredResult<T>> Send<T>(Dictionary<string, object?> payload, string endpoint,
        StructuredRequest request, OpenAiExecution execution,
        Func<CancellationToken, ValueTask<string?>>? taskCredential,
        Func<JsonElement, ValueTask<StructuredResult<T>>> process)
    {
        var cancellationToken = execution.Token;
        var warnings = execution.Warnings;
        StructuredResult<T> Fail(StructuredErrorKind kind, bool transient = false, HttpStatusCode? status = null, TimeSpan? retry = null) =>
            StructuredResult<T>.Failure(new StructuredError
            {
                Kind = kind,
                Message = $"Structured operation failed: {kind}.",
                IsTransient = transient,
                HttpStatusCode = status,
                RetryAfter = retry,
            }, execution.Metadata, warnings);
        execution.CheckCancellation();
        string? credential;
        try
        {
            var resolver = request.CredentialResolver ?? taskCredential ?? _options.CredentialResolver;
            credential = resolver is null ? null : await execution.Await(resolver(cancellationToken).AsTask()).ConfigureAwait(false);
        }
        catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch(Exception exception) when(exception is not OutOfMemoryException and not StackOverflowException)
        {
            return Fail(StructuredErrorKind.Authentication);
        }

        if(String.IsNullOrWhiteSpace(credential))
            return Fail(StructuredErrorKind.CredentialsMissing);

        if(credential.Any(c => c < 33 || c > 126))
            return Fail(StructuredErrorKind.Authentication);

        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(_options.BaseAddress, endpoint));
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);
        if(ResponseOptions(request).IdempotencyKey is { } idempotency)
            message.Headers.Add("Idempotency-Key", idempotency);

        message.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        execution.CheckCancellation();
        await execution.Progress(new() { Kind = StructuredProgressKind.Started }).ConfigureAwait(false);
        execution.CheckCancellation();
        try
        {
            using var response = await execution.Await(_http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken), value => value.Dispose()).ConfigureAwait(false);
            execution.Metadata = execution.Metadata with { ProviderRequestId = response.Headers.TryGetValues("x-request-id", out var ids) ? ids.FirstOrDefault() : null };
            using var stream = await execution.Await(response.Content.ReadAsStreamAsync(cancellationToken), value => value.Dispose()).ConfigureAwait(false);
            JsonDocument document;
            try
            {
                if(request.Stream && response.IsSuccessStatusCode)
                {
                    if(response.Content.Headers.ContentType?.MediaType != "text/event-stream")
                        return Fail(StructuredErrorKind.InvalidResponse);

                    document = await ReadEvents(stream, request, execution).ConfigureAwait(false);
                }
                else
                {
                    var limit = endpoint == "images/generations" ? _options.MaxImageResponseBytes : _options.MaxResponseBytes;
                    using var buffer = new MemoryStream();
                    var chunk = new byte[8192];
                    int count;
                    while((count = await execution.Await(stream.ReadAsync(chunk, cancellationToken).AsTask()).ConfigureAwait(false)) != 0)
                    {
                        if(buffer.Length + count > limit)
                            return response.IsSuccessStatusCode ? Fail(StructuredErrorKind.InvalidResponse) : HttpFailure(null);

                        buffer.Write(chunk, 0, count);
                    }

                    document = JsonDocument.Parse(buffer.ToArray());
                }
            }
            catch(JsonException)
            {
                return response.IsSuccessStatusCode ? Fail(StructuredErrorKind.InvalidResponse) : HttpFailure(null);
            }

            using(document)
            {
                var root = document.RootElement;
                execution.Metadata = execution.Metadata with
                {
                    ResponseId = Text(root, "id") ?? execution.Metadata.ResponseId,
                    ResolvedModel = Text(root, "model") ?? execution.Metadata.ResolvedModel,
                    RawResponse = ResponseOptions(request).CaptureRawResponse ? root : null,
                    Usage = ParseUsage(root, warnings),
                    CacheDiagnostics = ParseCacheDiagnostics(root, warnings),
                };
                if(!response.IsSuccessStatusCode)
                    return HttpFailure(Text(Property(root, "error"), "code"));

                execution.CheckCancellation();
                var result = await process(root).ConfigureAwait(false);
                execution.CheckCancellation();
                return result;
            }

            StructuredResult<T> HttpFailure(string? code)
            {
                var status = (int)response.StatusCode;
                var kind = ClassifyStatus(status);
                var retry = response.Headers.RetryAfter;
                var delay = retry?.Delta ?? (retry?.Date is { } date ? date - _options.TimeProvider.GetUtcNow() : (TimeSpan?)null);
                return Fail(kind, kind == StructuredErrorKind.ProviderUnavailable || kind == StructuredErrorKind.RateLimited && code != "insufficient_quota",
                    response.StatusCode, delay < TimeSpan.Zero ? TimeSpan.Zero : delay);
            }
        }
        catch(OperationCanceledException) when(!cancellationToken.IsCancellationRequested)
        {
            return Fail(StructuredErrorKind.TransportFailure, true);
        }
        catch(HttpRequestException)
        {
            return Fail(StructuredErrorKind.TransportFailure, true);
        }
        catch(IOException)
        {
            return Fail(StructuredErrorKind.TransportFailure, true);
        }
    }

}
