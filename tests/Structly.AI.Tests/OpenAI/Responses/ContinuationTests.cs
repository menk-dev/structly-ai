using Structly.AI.OpenAI;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Structly.AI.Tests.ResponseTestSupport;

namespace Structly.AI.Tests;

public sealed class ContinuationTests
{
    [Fact]
    public async Task ContinuationUsesNewTypeSchemaAndVocabularyAndRetainsBilledFailure()
    {
        var calls = 0;
        using var handler = new Handler(async message =>
        {
            using var document = JsonDocument.Parse(await message.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken));
            var root = document.RootElement;
            Assert.Equal("Current instructions", root.GetProperty("instructions").GetString());
            var properties = root.GetProperty("text").GetProperty("format").GetProperty("schema").GetProperty("properties");
            if(++calls == 1)
            {
                Assert.True(root.GetProperty("store").GetBoolean());
                Assert.True(properties.TryGetProperty("code", out _));
                return Response(Envelope("{\"code\":\"AB\"}"));
            }

            Assert.Equal("current", root.GetProperty("previous_response_id").GetString());
            Assert.Equal("current", root.GetProperty("prompt_cache_options").GetProperty("comparison_response_id").GetString());
            Assert.False(root.GetProperty("store").GetBoolean());
            Assert.Equal("new", properties.GetProperty("choice").GetProperty("enum")[0].GetString());
            var envelope = JsonNode.Parse(Envelope("{\"renamed\":\"xxx\",\"choice\":\"old\",\"items\":[null,null],\"number\":-3,\"optional\":null}"))!;
            envelope["prompt_cache_diagnostics"]!["type"] = "cache_miss";
            envelope["prompt_cache_diagnostics"]!["reason"] = "text_format_changed";
            return Response(envelope.ToJsonString());
        });
        using var http = new HttpClient(handler);
        var client = Client(http);
        var first = await client.ExecuteAsync(Contract<Patterned>(), new OpenAiRequest() { Input = "first", OpenAi = new() { Store = true } }, TestContext.Current.CancellationToken);
        Assert.True(first.IsSuccess);
        var second = await client.ExecuteAsync(Contract<Guided>(), new OpenAiRequest()
        {
            Input = "followup",
            Vocabularies = Vocabulary("new"),
            OpenAi = new() { Store = false, PreviousResponseId = first.Metadata.ResponseId, Cache = new() { ComparisonResponseId = first.Metadata.ResponseId } },
        }, TestContext.Current.CancellationToken);
        Assert.Equal(StructuredErrorKind.InvalidOutput, second.Error!.Kind);
        Assert.Equal(100, second.Metadata.Usage!.InputTokens);
        Assert.Equal("text_format_changed", second.Metadata.CacheDiagnostics!.Reason);
    }

}
