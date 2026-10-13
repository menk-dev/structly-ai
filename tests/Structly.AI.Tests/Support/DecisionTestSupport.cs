using Structly.AI.OpenAI;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace Structly.AI.Tests;

static class DecisionTestSupport
{
    internal static OpenAiClientOptions Options => new()
    {
        DefaultModel = new() { ModelId = "response-model" },
        DefaultDecisionModel = new() { ProfileName = "routing" },
        DecisionProfiles = new Dictionary<string, ModelSelection> { ["routing"] = new() { ModelId = "decision-model" } },
        CredentialResolver = Credentials.FromStatic("offline"),
    };

    internal static DecisionTask<TicketDecision> TicketTask(string instruction = "Route this ticket") => DecisionTask.Create<TicketDecision>(questions => questions
        .Ask(x => x.Department, instruction)
        .Ask(x => x.Blocking, "Is the customer blocked?")
        .Ask(x => x.Team, "Choose a team", choices: ["billing", "technical"])
        .Ask(x => x.Severity, "Rate severity", levels: [new("Cosmetic"), new("Workaround"), new("Blocked")]));

    internal static string Envelope => """
        {"model":"resolved-decision","answers":[
          {"type":"choice","name":"Department","choice":"billing","confidence":0.55,
           "probabilities":[{"value":"billing","probability":0.8},{"value":"Technical","probability":0.2}]},
          {"type":"predicate","name":"Blocking","probability":0.9},
          {"type":"choice","name":"Team","choice":"billing","confidence":0.6,
           "probabilities":[{"value":"billing","probability":0.7},{"value":"technical","probability":0.3}]},
          {"type":"score","name":"Severity","score":1.1,"confidence":0.55,
           "probabilities":[{"value":0,"label":"Cosmetic","probability":0.1},
                            {"value":1,"label":"Workaround","probability":0.7},
                            {"value":2,"label":"Blocked","probability":0.2}]}
        ],"usage":{"input_tokens":42,"output_tokens":0,"total_tokens":42}}
        """;

    public sealed record TicketDecision
    {
        [JsonPropertyName("route")]
        public required DecisionChoice<Department> Department { get; init; }
        public required DecisionPredicate Blocking { get; init; }
        public required DecisionChoice<string> Team { get; init; }
        public required DecisionScore Severity { get; init; }
        [JsonIgnore]
        public string Local => "ignored";
    }

    public enum Department
    {
        [Description("Payments and refunds"), JsonStringEnumMemberName("billing")]
        Billing,
        [Description("Product problems")]
        Technical,
    }
}
