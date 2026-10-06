using Structly.AI.OpenAI;
using System.Text.Json;
using static Structly.AI.Tests.ResponseTestSupport;

namespace Structly.AI.Tests;

public sealed class StorageTests
{
    [Theory]
    [InlineData(true, null, true)]
    [InlineData(false, null, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    [InlineData(false, false, false)]
    public async Task Requests_and_batches_use_request_storage_before_client_default(bool clientStore, bool? requestStore, bool expected)
    {
        using var handler = new Handler(async message =>
        {
            using var payload = JsonDocument.Parse(await message.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.Equal(expected, payload.RootElement.GetProperty("store").GetBoolean());
            return Response(Envelope("{\"code\":\"AB\"}"));
        });
        using var http = new HttpClient(handler);
        var client = new OpenAiClient(http, new()
        {
            DefaultModel = new() { ModelId = "response-model" },
            CredentialResolver = Credentials.FromStatic("offline"),
            Store = clientStore,
        });
        var request = new OpenAiRequest { Input = "input", OpenAi = new() { Store = requestStore, CaptureOutputText = true } };
        var task = Contract<Patterned>();
        Assert.True((await client.ExecuteAsync(task, request, TestContext.Current.CancellationToken)).IsSuccess);
        Assert.True((await client.GenerateTextAsync(new() { Request = request }, TestContext.Current.CancellationToken)).IsSuccess);
        var batch = client.PrepareResponseBatch(task, [new("item", request)]);
        using var line = JsonDocument.Parse(batch.Jsonl.Trim());
        Assert.Equal(expected, line.RootElement.GetProperty("body").GetProperty("store").GetBoolean());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Plain_requests_and_null_provider_options_inherit_storage(bool store)
    {
        using var handler = new Handler(async message =>
        {
            using var payload = JsonDocument.Parse(await message.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.Equal(store, payload.RootElement.GetProperty("store").GetBoolean());
            return Response(Envelope("{\"code\":\"AB\"}"));
        });
        using var http = new HttpClient(handler);
        var client = new OpenAiClient(http, new()
        {
            DefaultModel = new() { ModelId = "response-model" },
            CredentialResolver = Credentials.FromStatic("offline"),
            Store = store,
        });
        StructuredRequest[] requests = [new() { Input = "input" }, new OpenAiRequest { Input = "input", OpenAi = null }];
        foreach(var request in requests)
            Assert.True((await client.ExecuteAsync(Contract<Patterned>(), request, TestContext.Current.CancellationToken)).IsSuccess);
    }

    [Fact]
    public async Task Conversations_store_responses_when_ordinary_response_storage_is_disabled()
    {
        var calls = 0;
        using var handler = new Handler(async message =>
        {
            using var payload = JsonDocument.Parse(await message.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.True(payload.RootElement.GetProperty("store").GetBoolean());
            if(++calls == 2)
                Assert.Equal("current", payload.RootElement.GetProperty("previous_response_id").GetString());

            return Response(Envelope("{\"code\":\"AB\"}"));
        });
        using var http = new HttpClient(handler);
        var client = new OpenAiClient(http, new()
        {
            DefaultModel = new() { ModelId = "response-model" },
            CredentialResolver = Credentials.FromStatic("offline"),
            Store = false,
        });
        var conversation = client.CreateConversation(Contract<Patterned>());
        Assert.True((await conversation.ExecuteAsync(new() { Input = "first" }, TestContext.Current.CancellationToken)).IsSuccess);
        Assert.True((await conversation.ExecuteAsync(new() { Input = "second" }, TestContext.Current.CancellationToken)).IsSuccess);
        Assert.Equal(2, calls);
    }
}
