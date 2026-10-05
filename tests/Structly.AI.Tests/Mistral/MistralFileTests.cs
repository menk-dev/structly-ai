using Structly.AI.Mistral;
using System.Text;
using static Structly.AI.Tests.MistralFixture;

namespace Structly.AI.Tests;

public sealed class MistralFileTests
{
    [Fact]
    public async Task Files_upload_retrieve_download_and_delete_without_disposing_caller_streams()
    {
        using var handler = new Handler(request => Handler.Response(request.Method == HttpMethod.Delete ? "{\"deleted\":true}" :
            request.RequestUri!.AbsolutePath.EndsWith("/content", StringComparison.Ordinal) ? "file content" : """{"id":"input","filename":"batch.jsonl","bytes":null,"purpose":"batch"}"""));
        using var http = new HttpClient(handler);
        var client = new MistralClient(http, Options());
        using var source = new MemoryStream(Encoding.UTF8.GetBytes("{\"custom_id\":\"first\",\"body\":{}}\n"));
        Assert.Equal("input", (await client.UploadBatchFileAsync(source, cancellationToken: TestContext.Current.CancellationToken)).EnsureSuccess().Id);
        Assert.True(source.CanRead);
        Assert.Null((await client.GetFileAsync("input", cancellationToken: TestContext.Current.CancellationToken)).EnsureSuccess().Bytes);
        using var destination = new MemoryStream();
        Assert.True((await client.DownloadFileContentAsync("input", destination, cancellationToken: TestContext.Current.CancellationToken)).EnsureSuccess());
        Assert.True(destination.CanWrite);
        Assert.Equal("file content", Encoding.UTF8.GetString(destination.ToArray()));
        Assert.True((await client.DeleteFileAsync("input", cancellationToken: TestContext.Current.CancellationToken)).EnsureSuccess());
        Assert.Equal(new[] { HttpMethod.Post, HttpMethod.Get, HttpMethod.Get, HttpMethod.Delete }, handler.Methods);
    }

    [Fact]
    public async Task Invalid_remote_ids_fail_before_credentials_and_download_limits_are_enforced()
    {
        using var handler = new Handler("file content");
        using var http = new HttpClient(handler);
        var client = new MistralClient(http, Options());
        Assert.Equal(StructuredErrorKind.InvalidRequest, (await client.GetFileAsync("../other", cancellationToken: TestContext.Current.CancellationToken)).Error!.Kind);
        Assert.Empty(handler.Uris);
        using var destination = new MemoryStream();
        Assert.Equal(StructuredErrorKind.InvalidResponse, (await client.DownloadFileContentAsync("input", destination, new() { MaxDownloadBytes = 1 }, TestContext.Current.CancellationToken)).Error!.Kind);
    }
}
