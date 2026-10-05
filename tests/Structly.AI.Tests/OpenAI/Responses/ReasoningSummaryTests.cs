using System.Text;
using System.Text.Json;
using static Structly.AI.Tests.ResponseTestSupport;

namespace Structly.AI.Tests;

public sealed class ReasoningSummaryTests
{
    [Theory]
    [InlineData(false, true, "completed", "{\"code\":\"AB\"}", null)]
    [InlineData(true, true, "completed", "{\"code\":\"AB\"}", null)]
    [InlineData(false, false, "completed", "{\"code\":\"AB\"}", null)]
    [InlineData(false, true, "completed", "{}", StructuredErrorKind.InvalidOutput)]
    [InlineData(false, true, "incomplete", "partial", StructuredErrorKind.IncompleteOutput)]
    [InlineData(true, true, "failed", "partial", StructuredErrorKind.ProviderRejected)]
    public async Task FinalSummariesRespectOptInAndSurviveFailures(bool stream, bool include, string status, string text, StructuredErrorKind? error)
    {
        using var handler = new Handler(async message =>
        {
            using var payload = JsonDocument.Parse(await message.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.Equal(stream, payload.RootElement.GetProperty("stream").GetBoolean());
            if(include)
                Assert.Equal("auto", payload.RootElement.GetProperty("reasoning").GetProperty("summary").GetString());
            else
                Assert.False(payload.RootElement.TryGetProperty("reasoning", out _));

            var body = JsonSerializer.Serialize(new
            {
                id = "response",
                model = "resolved",
                status,
                output = new object[]
                {
                    new { type = "reasoning", summary = new object[]
                    {
                        new { type = "summary_text", text = "First" },
                        new { type = "reasoning_text", text = "RAW_SECRET" },
                        new { type = "summary_text", text = "Second" },
                    }, content = new[] { new { type = "reasoning_text", text = "RAW_SECRET" } }, },
                    new { type = "reasoning", summary = new[] { new { type = "summary_text", text = "Third" } } },
                    new { type = "message", role = "assistant", status = "completed", content = new[] { new { type = "output_text", text } } },
                },
            });
            var response = Response(body, stream);
            if(stream && status != "completed")
            {
                var events = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
                response.Content.Dispose();
                response.Content = new StringContent(events.Replace("response.completed", "response." + status, StringComparison.Ordinal), Encoding.UTF8, "text/event-stream");
            }

            return response;
        });
        using var http = new HttpClient(handler);
        var result = await Client(http).ExecuteAsync(Contract<Patterned>(), new()
        {
            Input = "read",
            Stream = stream,
            IncludeReasoningSummary = include,
        }, TestContext.Current.CancellationToken);
        Assert.Equal(error, result.Error?.Kind);
        Assert.Equal(include ? "First\nSecond\nThird" : null, result.Metadata.ReasoningSummary);
        Assert.Null(result.Metadata.OutputText);
        Assert.Null(result.Metadata.RawResponse);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConversationsCanRequestSummariesWithoutStreaming(bool stream)
    {
        using var handler = new Handler(async message =>
        {
            using var payload = JsonDocument.Parse(await message.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.Equal("auto", payload.RootElement.GetProperty("reasoning").GetProperty("summary").GetString());
            return Response(Envelope("{\"code\":\"AB\"}"), stream);
        });
        using var http = new HttpClient(handler);
        var conversation = Client(http).CreateConversation(Contract<Patterned>(), new() { IncludeReasoningSummary = true });
        var result = await conversation.ExecuteAsync(new() { Input = "read", Stream = stream }, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Null(result.Metadata.ReasoningSummary);
    }
}
