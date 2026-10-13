using Structly.AI.OpenAI;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using static Structly.AI.Tests.DecisionTestSupport;
using Handler = Structly.AI.Tests.ResponseTestSupport.Handler;

namespace Structly.AI.Tests;

public sealed class DecisionSetTests
{
    [Fact]
    public async Task SetsPreserveKeysAndTaskOrderAlongsideFixedAnswers()
    {
        var questions = new Dictionary<string, string> { ["helmet"] = "Has a helmet?", ["Helmet"] = "Has decoration?" };
        var task = DecisionTask.Create<Inspection>(q => q
            .Ask(x => x.Visible, "Visible?")
            .AskEach(x => x.Traits, questions)
            .AskEach(x => x.OtherTraits, new Dictionary<string, string> { ["helmet"] = "Other helmet?" })
            .AskEach(x => x.Conditions, new Dictionary<string, string> { ["base"] = "Condition?" })
            .AskEach(x => x.Empty, new Dictionary<string, string>()));
        questions["helmet"] = "changed";
        questions.Clear();
        using var handler = new Handler(async request =>
        {
            using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken));
            var sent = payload.RootElement.GetProperty("questions");
            Assert.Equal(5, sent.GetArrayLength());
            Assert.Equal("Has a helmet?", sent[1].GetProperty("instructions").GetString());
            Assert.Equal(2, sent[4].GetProperty("choices").GetArrayLength());
            return ResponseTestSupport.Response("""
                {"model":"resolved","answers":[
                    {"name":"Conditions:0","type":"choice","choice":true,"confidence":0.8,
                     "probabilities":[{"value":false,"probability":0.2},{"value":true,"probability":0.8}]},
                    {"name":"OtherTraits:0","type":"predicate","probability":0.1},
                    {"name":"Traits:1","type":"predicate","probability":0.4},
                    {"name":"Visible","type":"predicate","probability":1},
                    {"name":"Traits:0","type":"predicate","probability":0.9}]}
                """);
        });
        using var http = new HttpClient(handler);
        var value = (await new OpenAiClient(http, Options).DecideAsync(task, "photo", TestContext.Current.CancellationToken)).EnsureSuccess();
        Assert.Equal(1, value.Visible.Probability);
        Assert.Equal(new[] { "helmet", "Helmet" }, value.Traits.Select(x => x.Key));
        Assert.Equal(new[] { "helmet" }, value.Traits.Where(x => x.Answer.Probability >= 0.8).Select(x => x.Key));
        Assert.Equal(0.9, value.Traits["helmet"].Probability);
        Assert.True(value.Traits.TryGetValue("Helmet", out var answer));
        Assert.Equal(0.4, answer!.Probability);
        Assert.False(value.Traits.TryGetValue("HELMET", out _));
        Assert.Throws<KeyNotFoundException>(() => value.Traits["missing"]);
        Assert.Equal(0.1, value.OtherTraits["helmet"].Probability);
        Assert.True(value.Conditions["base"].Choice);
        Assert.Empty(value.Empty);
        Assert.Equal(2, value.Traits.Count);
        Assert.Equal(2, ((System.Collections.IEnumerable)value.Traits).Cast<object>().Count());
    }

    [Theory]
    [InlineData("missing", StructuredErrorKind.InvalidResponse)]
    [InlineData("extra", StructuredErrorKind.InvalidResponse)]
    [InlineData("duplicate", StructuredErrorKind.InvalidResponse)]
    [InlineData("unknown", StructuredErrorKind.InvalidResponse)]
    [InlineData("invalid", StructuredErrorKind.InvalidResponse)]
    [InlineData("refusal", StructuredErrorKind.Refused)]
    public async Task InvalidSetAnswersFailTheCompleteResult(string scenario, StructuredErrorKind expected)
    {
        var task = DecisionTask.Create<PredicateSet>(q => q.AskEach(x => x.Answers,
            new Dictionary<string, string> { ["first"] = "First?", ["second"] = "Second?" }));
        var root = JsonNode.Parse("""
            {"model":"resolved","answers":[
                {"name":"Answers:0","type":"predicate","probability":0.9},
                {"name":"Answers:1","type":"predicate","probability":0.1}],"usage":{"input_tokens":7}}
            """)!;
        var answers = (JsonArray)root["answers"]!;
        switch(scenario)
        {
            case "missing":
                answers.RemoveAt(0);
                break;
            case "extra":
                answers.Add(answers[0]!.DeepClone());
                break;
            case "duplicate":
                answers[1]!["name"] = "Answers:0";
                break;
            case "unknown":
                answers[1]!["name"] = "unknown";
                break;
            case "invalid":
                answers[1]!["probability"] = 2;
                break;
            case "refusal":
                answers[1] = new JsonObject { ["name"] = "Answers:1", ["type"] = "refusal" };
                break;
        }

        using var handler = new Handler(_ => Task.FromResult(ResponseTestSupport.Response(root.ToJsonString())));
        using var http = new HttpClient(handler);
        var result = await new OpenAiClient(http, Options).DecideAsync(task, "evidence", TestContext.Current.CancellationToken);
        Assert.Equal(expected, result.Error!.Kind);
        Assert.Null(result.Value);
        Assert.Equal(7, result.Metadata.Usage!.InputTokens);
    }

    [Fact]
    public void DefinitionsAreValidatedEvenForEmptySets()
    {
        Assert.Throws<ArgumentException>(() => DecisionTask.Create<PredicateSet>(_ => { }));
        Assert.Throws<ArgumentException>(() => DecisionTask.Create<PredicateSet>(q => q
            .AskEach(x => x.Answers, new Dictionary<string, string> { [" "] = "Question" })));
        Assert.Throws<ArgumentException>(() => DecisionTask.Create<PredicateSet>(q => q
            .AskEach(x => x.Answers, new Dictionary<string, string> { ["id"] = " " })));
        Assert.Throws<ArgumentException>(() => DecisionTask.Create<PredicateSet>(q => q
            .AskEach(x => x.Answers, new Dictionary<string, string>())
            .AskEach(x => x.Answers, new Dictionary<string, string>())));
        Assert.Throws<ArgumentException>(() => DecisionTask.Create<StringSet>(q => q
            .AskEach(x => x.Answers, new Dictionary<string, string>())));
        Assert.Throws<ArgumentException>(() => DecisionTask.Create<StringSet>(q => q
            .AskEach(x => x.Answers, new Dictionary<string, string>(), choices: ["one"])));
        Assert.Throws<ArgumentException>(() => DecisionTask.Create<UnsupportedSet>(_ => { }));
    }

    [Fact]
    public async Task ExplicitChoicesScoresAndConstructorBoundSetsWork()
    {
        var choices = new List<DecisionOption<string>> { new("good", "Intact"), new("bad", "Broken") };
        var levels = new List<DecisionLevel> { new("Low"), new("High") };
        var task = DecisionTask.Create<Rated>(q => q
            .AskEach(x => x.Choices, new Dictionary<string, string> { ["part"] = "Condition?" }, choices)
            .AskEach(x => x.Scores, new Dictionary<string, string> { ["paint"] = "Quality?" }, levels));
        choices.Clear();
        levels.Clear();
        using var handler = new Handler(_ => Task.FromResult(ResponseTestSupport.Response("""
            {"model":"resolved","answers":[
                {"name":"Choices:0","type":"choice","choice":"good","confidence":0.8,
                 "probabilities":[{"value":"good","probability":0.8},{"value":"bad","probability":0.2}]},
                {"name":"Scores:0","type":"score","score":0.7,"confidence":0.6,
                 "probabilities":[{"value":0,"label":"Low","probability":0.3},{"value":1,"label":"High","probability":0.7}]}]}
            """)));
        using var http = new HttpClient(handler);
        var value = (await new OpenAiClient(http, Options).DecideAsync(task, "evidence", TestContext.Current.CancellationToken)).EnsureSuccess();
        Assert.Equal("good", value.Choices["part"].Choice);
        Assert.Equal(0.7, value.Scores["paint"].Score);
        _ = DecisionTask.Create<StringSet>(q => q.AskEach(x => x.Answers,
            new Dictionary<string, string>(), choices: new[] { "good", "bad" }));
    }

    public sealed record Inspection
    {
        public required DecisionPredicate Visible { get; init; }
        [JsonPropertyName("traits")]
        public required DecisionSet<DecisionPredicate> Traits { get; init; }
        public required DecisionSet<DecisionPredicate> OtherTraits { get; init; }
        public required DecisionSet<DecisionChoice<bool>> Conditions { get; init; }
        public required DecisionSet<DecisionPredicate> Empty { get; init; }
    }
    public sealed record PredicateSet
    {
        public required DecisionSet<DecisionPredicate> Answers { get; init; }
    }
    public sealed record StringSet
    {
        public required DecisionSet<DecisionChoice<string>> Answers { get; init; }
    }
    public sealed record UnsupportedSet
    {
        public required DecisionSet<string> Answers { get; init; }
    }
    public sealed class Rated(DecisionSet<DecisionChoice<string>> choices, DecisionSet<DecisionScore> scores)
    {
        [JsonPropertyName("conditions")]
        public DecisionSet<DecisionChoice<string>> Choices { get; } = choices;
        public DecisionSet<DecisionScore> Scores { get; } = scores;
    }
}
