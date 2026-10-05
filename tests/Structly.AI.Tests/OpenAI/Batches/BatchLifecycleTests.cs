using Structly.AI.OpenAI;
using System.Net;
using System.Text;
using static Structly.AI.Tests.BatchTestSupport;

namespace Structly.AI.Tests;

public sealed class BatchLifecycleTests
{
    [Fact]
    public async Task MultipartSubmissionRetainsFileOnCreateFailureAndLeavesCallerStreamsOpen()
    {
        using var handler = new AsyncHandler(async request =>
        {
            if(request.RequestUri!.AbsolutePath.EndsWith("files", StringComparison.Ordinal))
            {
                Assert.IsType<MultipartFormDataContent>(request.Content);
                var body = await request.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken);
                Assert.Contains("batch", body);
                Assert.Contains("custom_id", body);
                return Response(new { id = "uploaded", filename = "batch.jsonl", bytes = 20, purpose = "batch" });
            }

            return new(HttpStatusCode.BadRequest) { Content = new StringContent("{}") };
        });
        using var http = new HttpClient(handler);
        var client = Client(http);
        var prepared = client.PrepareEmbeddingBatch([Item("a")]);
        using var source = new MemoryStream(Encoding.UTF8.GetBytes(prepared.Jsonl));
        Assert.True((await client.UploadBatchFileAsync(source, cancellationToken: TestContext.Current.CancellationToken)).IsSuccess);
        Assert.True(source.CanRead);
        var result = await client.SubmitBatchAsync(prepared, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("uploaded", result.Metadata.UploadedFileId);
        Assert.Equal(400, result.Error!.HttpStatusCodeValue);
    }

    [Fact]
    public async Task LifecycleParsingAndDownloadOwnership()
    {
        using var handler = new Handler(request => request.RequestUri!.AbsolutePath switch
        {
            "/v1/files/f/content" => new(HttpStatusCode.OK) { Content = new StringContent("content") },
            "/v1/files/f" when request.Method == HttpMethod.Delete => Response(new { deleted = true }),
            "/v1/files/f" => Response(new { id = "f", filename = "batch.jsonl", bytes = 12 }),
            "/v1/batches" when request.Method == HttpMethod.Get => Response(new { data = new[] { Job() }, has_more = true, last_id = "batch" }),
            _ => Response(Job("cancelling")),
        });
        using var http = new HttpClient(handler);
        var client = Client(http);
        var token = TestContext.Current.CancellationToken;
        Assert.Equal(12, (await client.GetFileAsync("f", cancellationToken: token)).EnsureSuccess().Bytes);
        Assert.True((await client.DeleteFileAsync("f", cancellationToken: token)).EnsureSuccess());
        using var destination = new MemoryStream();
        Assert.True((await client.DownloadFileContentAsync("f", destination, cancellationToken: token)).IsSuccess);
        Assert.True(destination.CanWrite);
        Assert.Equal("content", Encoding.UTF8.GetString(destination.ToArray()));
        Assert.True((await client.ListBatchesAsync(cancellationToken: token)).EnsureSuccess().HasMore);
        Assert.Equal("cancelling", (await client.CancelBatchAsync("batch", cancellationToken: token)).EnsureSuccess().Status);
    }

    [Fact]
    public async Task BatchTransportDeadlineAndCallerCancellationDisposeLateResponses()
    {
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new AsyncHandler(_ => pending.Task);
        using var http = new HttpClient(handler);
        var clock = new BatchClock();
        var client = new OpenAiClient(http, new() { DefaultModel = new() { ModelId = "model" }, CredentialResolver = Credentials.FromStatic("key"), TimeProvider = clock });
        var resultTask = client.GetBatchAsync("batch", new() { TotalTimeout = TimeSpan.FromSeconds(2) }, TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromSeconds(2));
        var result = await resultTask;
        Assert.Equal(StructuredErrorKind.DeadlineExceeded, result.Error!.Kind);
        Assert.Equal(TimeSpan.FromSeconds(2), result.Error.TotalTimeout);
        var content = new TrackedContent();
        pending.SetResult(new(HttpStatusCode.OK) { Content = content });
        await content.Disposed.Task.WaitAsync(TestContext.Current.CancellationToken);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var exception = await Assert.ThrowsAsync<StructuredOperationCanceledException>(() => client.GetBatchAsync("batch", cancellationToken: cancellation.Token));
        Assert.Equal(cancellation.Token, exception.CancellationToken);
    }

}
