using Structly.AI.OpenAI;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using static Structly.AI.Tests.DecisionTestSupport;
using Handler = Structly.AI.Tests.ResponseTestSupport.Handler;

namespace Structly.AI.Tests;

public sealed class DecisionResponseTests
{
    [Fact]
    public async Task MixedAnswersMapToPropertiesAndRetainAccounting()
    {
        StructuredUsageEvent? observed = null;
        using var handler = new Handler(async message =>
        {
            Assert.EndsWith("/decisions", message.RequestUri!.AbsoluteUri);
            using var doc = JsonDocument.Parse(await message.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken));
            var root = doc.RootElement;
            Assert.Equal("decision-model", root.GetProperty("model").GetString());
            Assert.Equal("ticket", root.GetProperty("input").GetString());
            Assert.Equal("user-id", root.GetProperty("safety_identifier").GetString());
            Assert.False(root.TryGetProperty("store", out _));
            Assert.False(root.TryGetProperty("stream", out _));
            Assert.False(root.TryGetProperty("instructions", out _));
            var question = root.GetProperty("questions")[0];
            Assert.Equal("Department", question.GetProperty("name").GetString());
            Assert.Equal("Contextual instructions", question.GetProperty("instructions").GetString());
            Assert.Equal("billing", question.GetProperty("choices")[0].GetProperty("value").GetString());
            Assert.Equal("Payments and refunds", question.GetProperty("choices")[0].GetProperty("description").GetString());
            var response = ResponseTestSupport.Response(Envelope);
            response.Headers.Add("x-request-id", "request-id");
            return response;
        });
        using var http = new HttpClient(handler);
        var result = await new OpenAiClient(http, Options).DecideAsync(TicketTask("Contextual instructions"), new OpenAiDecisionRequest
        {
            Input = "ticket",
            SafetyIdentifier = "user-id",
            CorrelationId = "local",
            CaptureRawResponse = true,
            UsageObserver = (usage, _) =>
            {
                observed = usage;
                return ValueTask.CompletedTask;
            },
        }, TestContext.Current.CancellationToken);
        var value = result.EnsureSuccess();
        Assert.Equal(Department.Billing, value.Department.Choice);
        Assert.Equal(0.55, value.Department.Confidence);
        Assert.Equal(0.8, value.Department.Probabilities[0].Probability);
        Assert.Equal(0.9, value.Blocking.Probability);
        Assert.Equal("billing", value.Team.Choice);
        Assert.Equal(1.1, value.Severity.Score);
        Assert.Equal(0.55, value.Severity.Confidence);
        Assert.Throws<NotSupportedException>(() => ((IList<DecisionProbability<Department>>)value.Department.Probabilities).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<DecisionLevelProbability>)value.Severity.Probabilities).Clear());
        Assert.Equal("Decisions", result.Metadata.Operation);
        Assert.Equal("decision-model", result.Metadata.RequestedModel);
        Assert.Equal("resolved-decision", result.Metadata.ResolvedModel);
        Assert.Equal("request-id", result.Metadata.ProviderRequestId);
        Assert.Equal("local", result.Metadata.CorrelationId);
        Assert.Null(result.Metadata.ResponseId);
        Assert.Equal(42, result.Metadata.Usage!.InputTokens);
        Assert.Equal(0, result.Metadata.Usage.OutputTokens);
        Assert.NotNull(result.Metadata.RawResponse);
        Assert.Equal(4, result.Metadata.RawResponse!.Value.GetProperty("answers").GetArrayLength());
        Assert.True(observed!.Succeeded);
        Assert.Equal(result.Metadata.ExecutionId, observed.Metadata.ExecutionId);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task AnswersAndDistributionsAreMappedByIdentityRatherThanPosition()
    {
        var root = JsonNode.Parse(Envelope)!;
        var answers = (JsonArray)root["answers"]!;
        var reordered = new JsonArray(answers.Reverse().Select(x => x!.DeepClone()).ToArray());
        var distribution = (JsonArray)reordered[0]!["probabilities"]!;
        reordered[0]!["probabilities"] = new JsonArray(distribution.Reverse().Select(x => x!.DeepClone()).ToArray());
        root["answers"] = reordered;
        using var handler = new Handler(_ => Task.FromResult(ResponseTestSupport.Response(root.ToJsonString())));
        using var http = new HttpClient(handler);
        var value = (await new OpenAiClient(http, Options).DecideAsync(TicketTask(), "ticket", TestContext.Current.CancellationToken)).EnsureSuccess();
        Assert.Equal(Department.Billing, value.Department.Choice);
        Assert.Equal(new[] { 0, 1, 2 }, value.Severity.Probabilities.Select(x => x.Value));
    }

    [Theory]
    [InlineData("missing-answer")]
    [InlineData("extra-answer")]
    [InlineData("duplicate-name")]
    [InlineData("unknown-name")]
    [InlineData("null-name")]
    [InlineData("wrong-type")]
    [InlineData("missing-model")]
    [InlineData("null-answer")]
    [InlineData("invalid-choice")]
    [InlineData("typed-choice")]
    [InlineData("invalid-confidence")]
    [InlineData("negative-probability")]
    [InlineData("infinite-probability")]
    [InlineData("missing-probability")]
    [InlineData("distribution-sum")]
    [InlineData("duplicate-option")]
    [InlineData("missing-option")]
    [InlineData("unknown-option")]
    [InlineData("score-range")]
    [InlineData("score-average")]
    [InlineData("score-index")]
    [InlineData("score-label")]
    [InlineData("score-duplicate")]
    [InlineData("refusal-and-invalid")]
    public async Task MalformedAnswersFailAndRetainUsage(string scenario)
    {
        var root = JsonNode.Parse(Envelope)!;
        var answers = (JsonArray)root["answers"]!;
        switch(scenario)
        {
            case "missing-answer":
                answers.RemoveAt(0);
                break;
            case "extra-answer":
                answers.Add(answers[0]!.DeepClone());
                break;
            case "duplicate-name":
                answers[1]!["name"] = "Department";
                break;
            case "unknown-name":
                answers[0]!["name"] = "secret";
                break;
            case "null-name":
                answers[0]!["name"] = null;
                break;
            case "wrong-type":
                answers[0]!["type"] = "predicate";
                break;
            case "missing-model":
                ((JsonObject)root).Remove("model");
                break;
            case "null-answer":
                answers[0] = null;
                break;
            case "invalid-choice":
                answers[0]!["choice"] = "secret";
                break;
            case "typed-choice":
                answers[0]!["choice"] = true;
                break;
            case "invalid-confidence":
                answers[0]!["confidence"] = 1.1;
                break;
            case "negative-probability":
                answers[1]!["probability"] = -0.1;
                break;
            case "infinite-probability":
                answers[1]!["probability"] = JsonNode.Parse("1e1000");
                break;
            case "missing-probability":
                ((JsonObject)answers[1]!).Remove("probability");
                break;
            case "distribution-sum":
                answers[0]!["probabilities"]![0]!["probability"] = 0.5;
                break;
            case "duplicate-option":
                answers[0]!["probabilities"]![1]!["value"] = "billing";
                break;
            case "missing-option":
                ((JsonArray)answers[0]!["probabilities"]!).RemoveAt(0);
                break;
            case "unknown-option":
                answers[0]!["probabilities"]![1]!["value"] = "secret";
                break;
            case "score-range":
                answers[3]!["score"] = 3;
                break;
            case "score-average":
                answers[3]!["score"] = 1;
                break;
            case "score-index":
                answers[3]!["probabilities"]![0]!["value"] = 0.5;
                break;
            case "score-label":
                answers[3]!["probabilities"]![0]!["label"] = "secret";
                break;
            case "score-duplicate":
                answers[3]!["probabilities"]![1]!["value"] = 0;
                break;
            case "refusal-and-invalid":
                answers[0] = new JsonObject { ["name"] = "Department", ["type"] = "refusal" };
                answers[1]!["probability"] = 2;
                break;
        }

        StructuredUsageEvent? observed = null;
        using var handler = new Handler(_ => Task.FromResult(ResponseTestSupport.Response(root.ToJsonString())));
        using var http = new HttpClient(handler);
        var result = await new OpenAiClient(http, Options).DecideAsync(TicketTask(), new OpenAiDecisionRequest
        {
            Input = "ticket",
            UsageObserver = (usage, _) =>
            {
                observed = usage;
                return ValueTask.CompletedTask;
            },
        }, TestContext.Current.CancellationToken);
        Assert.Equal(StructuredErrorKind.InvalidResponse, result.Error!.Kind);
        Assert.Null(result.Value);
        Assert.Equal(42, result.Metadata.Usage!.InputTokens);
        Assert.Equal(StructuredErrorKind.InvalidResponse, observed!.FailureKind);
        Assert.DoesNotContain("secret", result.Error.Message);
        Assert.Null(result.Metadata.RawResponse);
    }

    [Fact]
    public async Task AnyRefusalFailsTheCompleteResultWithUsage()
    {
        var root = JsonNode.Parse(Envelope)!;
        root["answers"]![0] = new JsonObject { ["name"] = "Department", ["type"] = "refusal" };
        using var handler = new Handler(_ => Task.FromResult(ResponseTestSupport.Response(root.ToJsonString())));
        using var http = new HttpClient(handler);
        var result = await new OpenAiClient(http, Options).DecideAsync(TicketTask(), "ticket", TestContext.Current.CancellationToken);
        Assert.Equal(StructuredErrorKind.Refused, result.Error!.Kind);
        Assert.Null(result.Value);
        Assert.Equal(42, result.Metadata.Usage!.InputTokens);
    }

    [Fact]
    public async Task ConstructorBoundAnswersAndJsonNamesWork()
    {
        var task = DecisionTask.Create<ConstructorDto>(q => q.Ask(x => x.Answer, "condition"));
        using var handler = new Handler(_ => Task.FromResult(ResponseTestSupport.Response("""
            {"model":"resolved","answers":[{"name":"Answer","type":"predicate","probability":0.7}]}
            """)));
        using var http = new HttpClient(handler);
        var result = await new OpenAiClient(http, Options).DecideAsync(task, "ticket", TestContext.Current.CancellationToken);
        Assert.Equal(0.7, result.EnsureSuccess().Answer.Probability);
    }

    [Fact]
    public async Task ThrowingConstructorIsSanitizedInvalidOutputWithUsage()
    {
        var task = DecisionTask.Create<ThrowingDto>(q => q.Ask(x => x.Answer, "condition"));
        using var handler = new Handler(_ => Task.FromResult(ResponseTestSupport.Response("""
            {"model":"resolved","answers":[{"name":"Answer","type":"predicate","probability":0.7}],"usage":{"input_tokens":7}}
            """)));
        using var http = new HttpClient(handler);
        var result = await new OpenAiClient(http, Options).DecideAsync(task, "ticket", TestContext.Current.CancellationToken);
        Assert.Equal(StructuredErrorKind.InvalidOutput, result.Error!.Kind);
        Assert.DoesNotContain("secret", result.Error.Message);
        Assert.Equal(7, result.Metadata.Usage!.InputTokens);
    }

    [Fact]
    public async Task DistributionRoundingToleranceDoesNotRepairProviderValues()
    {
        var root = JsonNode.Parse(Envelope)!;
        root["answers"]![0]!["probabilities"]![0]!["probability"] = 0.80001;
        root["answers"]![3]!["score"] = 1.10001;
        using var handler = new Handler(_ => Task.FromResult(ResponseTestSupport.Response(root.ToJsonString())));
        using var http = new HttpClient(handler);
        var result = await new OpenAiClient(http, Options).DecideAsync(TicketTask(), "ticket", TestContext.Current.CancellationToken);
        Assert.Equal(0.80001, result.EnsureSuccess().Department.Probabilities[0].Probability);
        Assert.Equal(1.10001, result.Value!.Severity.Score);
    }

    public sealed class ConstructorDto
    {
        [JsonConstructor]
        public ConstructorDto(DecisionPredicate answer) => Answer = answer;
        [JsonPropertyName("different")]
        public DecisionPredicate Answer { get; }
    }
    public sealed class ThrowingDto
    {
        public ThrowingDto(DecisionPredicate answer) => throw new OperationCanceledException("secret");
        public DecisionPredicate Answer { get; }
    }
}
