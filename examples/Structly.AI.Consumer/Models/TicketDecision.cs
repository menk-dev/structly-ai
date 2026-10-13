using Structly.AI.OpenAI;

public sealed record TicketDecision
{
    public required DecisionChoice<Department> Department { get; init; }
    public required DecisionPredicate Blocking { get; init; }
    public required DecisionChoice<string> Team { get; init; }
}
