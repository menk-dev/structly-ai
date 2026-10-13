using Structly.AI;
using Structly.AI.OpenAI;

static class DecisionExamples
{
    public static async Task Run()
    {
        // Instructions and custom choices can come from application context.
        var routingInstructions = "Which department should handle this billing support request?";
        var teams = new[] { new DecisionOption<string>("payments", "Billing investigations."), new("support", "General product support.") };
        var task = DecisionTask.Create<TicketDecision>(questions => questions
            .Ask(x => x.Department, routingInstructions)
            .Ask(x => x.Blocking, "Does this prevent the customer from working?")
            .Ask(x => x.Team, "Which team should investigate?", teams));

        using var http = new HttpClient(new OfflineDecisionsHandler()) { Timeout = Timeout.InfiniteTimeSpan };
        var client = new OpenAiClient(http, new()
        {
            DefaultModel = new() { ModelId = "offline-response" },
            DefaultDecisionModel = new() { ModelId = "offline-decision" },
            CredentialResolver = Credentials.FromStatic("offline-fixture-credential"),
        });
        var result = await client.DecideAsync(task, "Invoice INV-42 was charged twice.");
        var answer = result.EnsureSuccess();
        if(answer.Department.Choice != Department.Billing || answer.Team.Choice != "payments" || result.Metadata.Usage?.InputTokens != 42)
            throw new InvalidOperationException("Decision answers and accounting must be retained.");

        Console.WriteLine($"Department: {answer.Department.Choice}, confidence: {answer.Department.Confidence}");
        Console.WriteLine($"Team: {answer.Team.Choice}, blocking probability: {answer.Blocking.Probability}");
    }
}
