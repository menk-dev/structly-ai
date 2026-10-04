using System.ComponentModel;
using System.Text.Json;
using Structly.AI;
using Structly.AI.OpenAI;
using Structly.AI.Testing;

// Runnable offline: no provider connection or real credentials. For production configuration
// use a normal HttpClient and Credentials.FromEnvironment as shown in the README.
var task = StructuredTask.Create<Ticket>(new()
{
    SchemaName = "ticket",
    Instructions = "Extract a support ticket and select the matching queue."
});
IReadOnlyDictionary<string, IReadOnlyList<string>> vocabularies = new Dictionary<string, IReadOnlyList<string>>
{
    ["queues"] = ["billing", "technical"]
};
if (task.CreateSchema(vocabularies).GetProperty("type").GetString() != "object")
    throw new InvalidOperationException("Expected an object schema.");
Console.WriteLine(task.CreateOutputSpecification(new(), vocabularies));
var example = task.CreateExample(vocabularies);
var completed = ResponseEnvelopes.CompleteExample(task, "{\"summary\":\"Example\"}", vocabularies);
if (!task.ReadOutput(example.GetRawText(), vocabularies).IsSuccess || !task.ReadOutput(completed.GetRawText(), vocabularies).IsSuccess)
    throw new InvalidOperationException("Examples must validate.");

using var http = new HttpClient(new OfflineResponsesHandler()) { Timeout = Timeout.InfiniteTimeSpan };
var client = new OpenAiClient(http, new()
{
    DefaultModel = new() { ModelId = "offline-fixture-model" },
    CredentialResolver = Credentials.FromStatic("offline-fixture-credential")
});
var observed = 0;
var result = await client.ExecuteAsync(task, new()
{
    Input = "Invoice INV-42 was charged twice.",
    Vocabularies = vocabularies,
    UsageObserver = (usage, token) =>
    {
        observed++;
        Console.WriteLine($"Usage {usage.Metadata.ExecutionId}: {usage.Metadata.Usage?.TotalTokens} tokens");
        return ValueTask.CompletedTask;
    }
});
var ticket = result.EnsureSuccess();
if (ticket.Queue != "billing" || result.Metadata.ResponseId != "resp_offline" || observed != 1)
    throw new InvalidOperationException("The packed consumer did not retain output and accounting metadata.");
Console.WriteLine($"Ticket: {ticket.Summary} ({ticket.Queue})");

var invalid = task.ReadOutput("{}", vocabularies);
if (invalid.Error?.Kind != StructuredErrorKind.InvalidOutput)
    throw new InvalidOperationException("Missing required output properties must fail validation.");
using var cancellation = new CancellationTokenSource();
cancellation.Cancel();
try
{
    await client.ExecuteAsync(task, new() { Input = "cancelled", Vocabularies = vocabularies }, cancellation.Token);
    throw new InvalidOperationException("Caller cancellation must throw.");
}
catch (StructuredOperationCanceledException exception) when (exception.CancellationToken == cancellation.Token)
{
    Console.WriteLine("Caller cancellation preserved.");
}
await BatchExamples.Run();
Console.WriteLine("Offline consumer passed.");

public sealed record Ticket
{
    [Description("Short description of the reported issue.")]
    public required string Summary { get; init; }
    [DynamicVocabulary("queues")]
    public required string Queue { get; init; }
    public string? Reference { get; init; }
}

sealed class OfflineResponsesHandler : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri?.AbsolutePath != "/v1/responses") throw new InvalidOperationException("Unexpected endpoint.");
        using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
        if (body.RootElement.GetProperty("text").GetProperty("format").GetProperty("strict").GetBoolean() != true)
            throw new InvalidOperationException("The consumer must send a strict schema.");
        var output = JsonSerializer.Serialize(new { summary = "Duplicate charge", queue = "billing", reference = "INV-42" });
        return ResponseEnvelopes.ToHttpResponse(ResponseEnvelopes.CompletedText(output, "offline-fixture-model", "resp_offline", new() { InputTokens = 12, OutputTokens = 8, TotalTokens = 20 }));
    }
}
