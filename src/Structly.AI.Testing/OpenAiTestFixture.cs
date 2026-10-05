using Structly.AI.OpenAI;
using System.Net;
using System.Text.Json;

namespace Structly.AI.Testing;

/// <summary>Runs the real OpenAI client against queued offline envelopes. Dispose after all operations finish.</summary>
public sealed class OpenAiTestFixture : IDisposable
{
    readonly FixtureHandler _handler;
    readonly HttpClient _http;
    bool _disposed;

    /// <summary>Creates an offline client with a test model and static fake credentials.</summary>
    public OpenAiTestFixture() : this(null, null) { }

    /// <summary>Creates an offline client with construction-time options and an optional asynchronous request responder.</summary>
    /// <remarks>Queued responses take precedence. The responder runs outside the capture lock and owns creation of a fresh response for each request.</remarks>
    public OpenAiTestFixture(OpenAiClientOptions? options,
        Func<CapturedRequest, CancellationToken, ValueTask<HttpResponseMessage>>? responder = null)
    {
        _handler = new(responder);
        _http = new(_handler) { Timeout = Timeout.InfiniteTimeSpan };
        Client = new(_http, options ?? new()
        {
            DefaultModel = new() { ModelId = "test-model" },
            CredentialResolver = Credentials.FromStatic("test-credential"),
        });
    }

    /// <summary>Gets the real client. Its transport never connects to a provider.</summary>
    public OpenAiClient Client { get; }
    /// <summary>Gets a detached snapshot of requests in arrival order, including requests with no queued response.</summary>
    public IReadOnlyList<CapturedRequest> Requests => _handler.Requests;
    /// <summary>Gets the number of responses not yet consumed.</summary>
    public int PendingResponseCount => _handler.PendingResponseCount;

    /// <summary>Queues a copied envelope and HTTP status. Each request consumes one response in arrival order.</summary>
    public void Enqueue(JsonElement envelope, HttpStatusCode status = HttpStatusCode.OK)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _handler.Enqueue(envelope.Clone(), status);
    }

    /// <summary>Disposes the HTTP client and handler. Captured request snapshots remain readable.</summary>
    public void Dispose()
    {
        if(_disposed)
            return;

        _disposed = true;
        _http.Dispose();
    }

    sealed class FixtureHandler(Func<CapturedRequest, CancellationToken, ValueTask<HttpResponseMessage>>? responder) : HttpMessageHandler
    {
        readonly object _gate = new();
        readonly Queue<(JsonElement Envelope, HttpStatusCode Status)> _responses = new();
        readonly List<CapturedRequest> _requests = [];

        public IReadOnlyList<CapturedRequest> Requests
        {
            get
            {
                lock(_gate)
                    return Array.AsReadOnly(_requests.ToArray());
            }
        }

        public int PendingResponseCount
        {
            get
            {
                lock(_gate)
                    return _responses.Count;
            }
        }

        public void Enqueue(JsonElement envelope, HttpStatusCode status)
        {
            lock(_gate)
                _responses.Enqueue((envelope, status));
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var captured = new CapturedRequest { Method = request.Method, Uri = request.RequestUri!, Body = body };
            lock(_gate)
            {
                _requests.Add(captured);
                if(_responses.TryDequeue(out var response))
                    return ResponseEnvelopes.ToHttpResponse(response.Envelope, response.Status);
            }

            if(responder is not null)
                return await responder(captured, cancellationToken).ConfigureAwait(false);

            throw new InvalidOperationException("No response is queued in OpenAiTestFixture. Call Enqueue before executing a request or configure a responder.");
        }
    }
}
