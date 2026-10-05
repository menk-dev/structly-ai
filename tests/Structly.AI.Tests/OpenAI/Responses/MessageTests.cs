using Structly.AI.OpenAI;
using System.Text.Json;
using static Structly.AI.Tests.ResponseTestSupport;

namespace Structly.AI.Tests;

public sealed class MessageTests
{
    [Fact]
    public async Task MultimodalRolesOrderGuidanceAndBreakpointsMatchWire()
    {
        var task = Contract<Guided>();
        var vocabulary = Vocabulary("b", "a");
        using var handler = new Handler(async message =>
        {
            using var document = JsonDocument.Parse(await message.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken));
            var root = document.RootElement;
            var messages = root.GetProperty("input");
            Assert.Equal(5, messages.GetArrayLength());
            Assert.Equal("system", messages[0].GetProperty("role").GetString());
            Assert.Equal("developer", messages[1].GetProperty("role").GetString());
            Assert.Equal("input_text", messages[0].GetProperty("content")[0].GetProperty("type").GetString());
            Assert.Equal("output_text", messages[3].GetProperty("content")[0].GetProperty("type").GetString());
            Assert.Equal(1, messages[2].GetProperty("content").GetArrayLength());
            var content = messages[4].GetProperty("content");
            Assert.Equal(4, content.GetArrayLength());
            Assert.Equal("question", content[0].GetProperty("text").GetString());
            Assert.Equal("input_image", content[1].GetProperty("type").GetString());
            Assert.Equal("https://example.test/a.png", content[1].GetProperty("image_url").GetString());
            Assert.Equal("high", content[1].GetProperty("detail").GetString());
            Assert.Equal("explicit", content[1].GetProperty("prompt_cache_breakpoint").GetProperty("mode").GetString());
            Assert.Equal("data:image/png;base64,AQ==", content[2].GetProperty("image_url").GetString());
            Assert.Equal(task.CreateOutputSpecification(new(), vocabulary), content[3].GetProperty("text").GetString());
            Assert.Equal("explicit", root.GetProperty("prompt_cache_options").GetProperty("mode").GetString());
            return Response(Envelope("{\"renamed\":\"xxx\",\"choice\":\"a\",\"items\":[null,null],\"number\":-3,\"optional\":null}"));
        });
        using var http = new HttpClient(handler);
        var result = await Client(http).ExecuteAsync(task, new OpenAiRequest()
        {
            Messages = [new(MessageRole.System, [new TextPart("system")]), new(MessageRole.Developer, [new TextPart("developer")]),
                new(MessageRole.User, [new TextPart("first")]), new(MessageRole.Assistant, [new TextPart("earlier")]),
                new(MessageRole.User, [new TextPart("question"), new ImagePart { Url = "https://example.test/a.png", Detail = ImageDetail.High, CacheBreakpoint = true }, new ImagePart { Url = "data:image/png;base64,AQ==" }])],
            Vocabularies = vocabulary,
            OutputSpecification = new(),
            OpenAi = new() { Cache = new() { Mode = OpenAiCacheMode.Explicit } },
        }, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
    }

    [Theory]
    [InlineData("both")]
    [InlineData("neither")]
    [InlineData("empty-messages")]
    [InlineData("empty-content")]
    [InlineData("role")]
    [InlineData("null-part")]
    [InlineData("empty-text")]
    [InlineData("detail")]
    [InlineData("http-image")]
    [InlineData("relative-image")]
    [InlineData("image-auth")]
    [InlineData("invalid-base64")]
    [InlineData("empty-base64")]
    [InlineData("unsupported-media")]
    [InlineData("assistant-image")]
    [InlineData("assistant-breakpoint")]
    [InlineData("guidance-no-user")]
    [InlineData("guidance-pattern")]
    [InlineData("text-vocabulary")]
    [InlineData("text-guidance")]
    public async Task UnsupportedInputsFailBeforeAuth(string scenario)
    {
        var auth = 0;
        using var handler = new Handler(_ => throw new InvalidOperationException("Should not send"));
        using var http = new HttpClient(handler);
        var request = new OpenAiRequest
        {
            Messages = [new(MessageRole.User, [new TextPart("prompt")])],
            CredentialResolver = _ =>
        {
            auth++;
            return ValueTask.FromResult<string?>("key");
        },
        };
        request = scenario switch
        {
            "both" => request with { Input = "prompt" },
            "neither" => request with { Messages = null },
            "empty-messages" => request with { Messages = [] },
            "empty-content" => request with { Messages = [new(MessageRole.User, [])] },
            "role" => request with { Messages = [new((MessageRole)100, [new TextPart("prompt")])] },
            "null-part" => request with { Messages = [new(MessageRole.User, [null!])] },
            "empty-text" => request with { Messages = [new(MessageRole.User, [new TextPart(" ")])] },
            "assistant-image" => request with { Messages = [new(MessageRole.Assistant, [new ImagePart { Url = "https://example.test/a" }])] },
            "assistant-breakpoint" => request with { Messages = [new(MessageRole.Assistant, [new TextPart("x") { CacheBreakpoint = true }])] },
            "guidance-no-user" => request with { Messages = [new(MessageRole.Developer, [new TextPart("x")])], OutputSpecification = new() { IncludeExample = false } },
            "guidance-pattern" or "text-guidance" => request with { OutputSpecification = new() },
            "text-vocabulary" => request with { Vocabularies = Vocabulary("a") },
            _ => request with
            {
                Messages = [new(MessageRole.User, [new ImagePart { Detail = scenario == "detail" ? (ImageDetail)100 : ImageDetail.Auto,
                Url = scenario switch { "http-image" => "http://example.test/a", "relative-image" => "/a", "image-auth" => "https://user:pass@example.test/a",
                    "invalid-base64" => "data:image/png;base64,!!", "empty-base64" => "data:image/png;base64,", "unsupported-media" => "data:image/svg+xml;base64,AQ==", _ => "https://example.test/a", }, }])],
            },
        };
        var error = scenario.StartsWith("text-", StringComparison.Ordinal) ? (await Client(http).GenerateTextAsync(new() { Request = request }, TestContext.Current.CancellationToken)).Error
            : (await Client(http).ExecuteAsync(Contract<Patterned>(), request, TestContext.Current.CancellationToken)).Error;
        Assert.Equal(StructuredErrorKind.InvalidRequest, error!.Kind);
        Assert.Equal(0, auth);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task CallerMessagesAndMetadataAreSnapshottedBeforeCredentials()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var parts = new List<ContentPart> { new TextPart("original") };
        var messages = new List<StructuredMessage> { new(MessageRole.User, parts) };
        var metadata = new Dictionary<string, string> { ["job"] = "original" };
        using var handler = new Handler(async message =>
        {
            using var document = JsonDocument.Parse(await message.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.Equal("original", document.RootElement.GetProperty("input")[0].GetProperty("content")[0].GetProperty("text").GetString());
            Assert.Equal("original", document.RootElement.GetProperty("metadata").GetProperty("job").GetString());
            return Response(Envelope());
        });
        using var http = new HttpClient(handler);
        var pending = Client(http).GenerateTextAsync(new()
        {
            Request = new OpenAiRequest()
            {
                Messages = messages,
                OpenAi = new() { Metadata = metadata },
                CredentialResolver = _ =>
                {
                    entered.SetResult();
                    return new(release.Task);
                },
            },
        }, TestContext.Current.CancellationToken);
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        parts[0] = new TextPart("mutated");
        messages.Clear();
        metadata["job"] = "mutated";
        release.SetResult("key");
        Assert.True((await pending).IsSuccess);
    }

}
