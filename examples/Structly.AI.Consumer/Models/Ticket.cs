using Structly.AI;
using System.ComponentModel;

public sealed record Ticket
{
    [Description("Short description of the reported issue.")]
    public required string Summary { get; init; }
    [DynamicVocabulary("queues")]
    public required string Queue { get; init; }
    public string? Reference { get; init; }
}
