using Structly.AI;
using Structly.AI.OpenAI;
using Structly.AI.Testing;

// Runnable offline: no provider connection or real credentials. For production configuration
// use a normal HttpClient and Credentials.FromEnvironment as shown in the README.
// Define the output contract and runtime vocabulary.
var task = StructuredTask.Create<Ticket>(new()
{
    SchemaName = "ticket",
    Instructions = "Extract a support ticket and select the matching queue.",
});

IReadOnlyDictionary<string, IReadOnlyList<string>> vocabularies = new Dictionary<string, IReadOnlyList<string>>
{
    ["queues"] = ["billing", "technical"],
};

// Inspect the schema and validate generated fixtures without HTTP.
if(task.CreateSchema(vocabularies).GetProperty("type").GetString() != "object")
    throw new InvalidOperationException("Expected an object schema.");

Console.WriteLine(task.CreateOutputSpecification(new(), vocabularies));
var example = task.CreateExample(vocabularies);
var completed = ResponseEnvelopes.CompleteExample(task, "{\"summary\":\"Example\"}", vocabularies);
if(!task.ReadOutput(example.GetRawText(), vocabularies).IsSuccess
    || !task.ReadOutput(completed.GetRawText(), vocabularies).IsSuccess)
    throw new InvalidOperationException("Examples must validate.");

// Execute through a simulated provider and record its reported usage.
using var http = new HttpClient(new OfflineResponsesHandler()) { Timeout = Timeout.InfiniteTimeSpan };
var client = new OpenAiClient(http, new()
{
    DefaultModel = new() { ModelId = "offline-fixture-model" },
    CredentialResolver = Credentials.FromStatic("offline-fixture-credential"),
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
    },
});

var ticket = result.EnsureSuccess();
if(ticket.Queue != "billing" || result.Metadata.ResponseId != "resp_offline" || observed != 1)
    throw new InvalidOperationException("The packed consumer did not retain output and accounting metadata.");
Console.WriteLine($"Ticket: {ticket.Summary} ({ticket.Queue})");

// Local validation rejects omitted required properties.
var invalid = task.ReadOutput("{}", vocabularies);
if(invalid.Error?.Kind != StructuredErrorKind.InvalidOutput)
    throw new InvalidOperationException("Missing required output properties must fail validation.");

// Caller cancellation throws and preserves the original token.
using var cancellation = new CancellationTokenSource();
cancellation.Cancel();
try
{
    await client.ExecuteAsync(task, new() { Input = "cancelled", Vocabularies = vocabularies }, cancellation.Token);
    throw new InvalidOperationException("Caller cancellation must throw.");
}
catch(StructuredOperationCanceledException exception) when(exception.CancellationToken == cancellation.Token)
{
    Console.WriteLine("Caller cancellation preserved.");
}

// Demonstrate batch import and usage deduplication separately.
await BatchExamples.Run();
Console.WriteLine("Offline consumer passed.");
