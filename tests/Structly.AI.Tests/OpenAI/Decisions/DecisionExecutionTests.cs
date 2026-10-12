using Structly.AI.OpenAI;
using System.Net;
using System.Text.Json;
using static Structly.AI.Tests.DecisionTestSupport;
using Handler = Structly.AI.Tests.ResponseTestSupport.Handler;

namespace Structly.AI.Tests;

public sealed class DecisionExecutionTests
{
    [Theory]
    [InlineData("blank-input")]
    [InlineData("both-inputs")]
    [InlineData("empty-messages")]
    [InlineData("role")]
    [InlineData("null-message")]
    [InlineData("empty-content")]
    [InlineData("null-part")]
    [InlineData("hosted-image")]
    [InlineData("bad-base64")]
    [InlineData("null-image")]
    [InlineData("too-many-images")]
    [InlineData("cache-breakpoint")]
    [InlineData("timeout")]
    [InlineData("model")]
    [InlineData("reasoning")]
    [InlineData("safety")]
    public async Task InvalidRequestsDoNotResolveCredentialsOrSend(string scenario)
    {
        var auth = 0;
        var request = new OpenAiDecisionRequest
        {
            Input = "ticket",
            CredentialResolver = _ =>
        {
            auth++;
            return ValueTask.FromResult<string?>("offline");
        },
        };
        request = scenario switch
        {
            "blank-input" => request with { Input = " " },
            "both-inputs" => request with { Messages = [new(MessageRole.User, [new TextPart("text")])] },
            "empty-messages" => request with { Input = null, Messages = [] },
            "role" => request with { Input = null, Messages = [new(MessageRole.System, [new TextPart("text")])] },
            "null-message" => request with { Input = null, Messages = [null!] },
            "empty-content" => request with { Input = null, Messages = [new(MessageRole.User, [])] },
            "null-part" => request with { Input = null, Messages = [new(MessageRole.User, [null!])] },
            "hosted-image" => request with { Input = null, Messages = [new(MessageRole.User, [new ImagePart { Url = "https://example.com/image.png" }])] },
            "bad-base64" => request with { Input = null, Messages = [new(MessageRole.User, [new ImagePart { Url = "data:image/png;base64,not-base64" }])] },
            "null-image" => request with { Input = null, Messages = [new(MessageRole.User, [new ImagePart { Url = null! }])] },
            "too-many-images" => request with { Input = null, Messages = [new(MessageRole.User, Enumerable.Repeat<ContentPart>(ImagePart.FromBytes([255, 216, 255]), 129).ToArray())] },
            "cache-breakpoint" => request with { Input = null, Messages = [new(MessageRole.User, [new TextPart("text") { CacheBreakpoint = true }])] },
            "timeout" => request with { TotalTimeout = TimeSpan.Zero },
            "model" => request with { ModelSelection = new() { ProfileName = "unknown" } },
            "reasoning" => request with { ModelSelection = new() { ModelId = "decision", ReasoningEffort = ReasoningEffort.High } },
            _ => request with { SafetyIdentifier = new string('a', 129) },
        };
        using var handler = new Handler(_ => throw new InvalidOperationException("Should not send"));
        using var http = new HttpClient(handler);
        var result = await new OpenAiClient(http, Options).DecideAsync(TicketTask(), request, TestContext.Current.CancellationToken);
        Assert.Equal(StructuredErrorKind.InvalidRequest, result.Error!.Kind);
        Assert.Equal(0, auth);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task DecisionsRequireTheirOwnModelAndPerCallSelectionTakesPrecedence()
    {
        using var handler = new Handler(async message =>
        {
            using var doc = JsonDocument.Parse(await message.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.Equal("explicit-decision", doc.RootElement.GetProperty("model").GetString());
            return ResponseTestSupport.Response(Envelope);
        });
        using var http = new HttpClient(handler);
        var client = new OpenAiClient(http, Options with { DefaultDecisionModel = null });
        var failed = await client.DecideAsync(TicketTask(), "ticket", TestContext.Current.CancellationToken);
        Assert.Equal(StructuredErrorKind.InvalidRequest, failed.Error!.Kind);
        Assert.Equal(0, handler.Calls);
        var succeeded = await client.DecideAsync(TicketTask(), new OpenAiDecisionRequest
        {
            Input = "ticket",
            ModelSelection = new() { ModelId = "explicit-decision" },
        }, TestContext.Current.CancellationToken);
        Assert.True(succeeded.IsSuccess);
    }

    [Fact]
    public void DecisionProfilesAndDefaultsAreValidatedAtClientConstruction()
    {
        using var http = new HttpClient();
        Assert.Throws<ArgumentException>(() => new OpenAiClient(http, Options with { DefaultDecisionModel = new() { ProfileName = "unknown" } }));
        Assert.Throws<ArgumentException>(() => new OpenAiClient(http, Options with { DefaultDecisionModel = new() { ModelId = "decision", ReasoningEffort = ReasoningEffort.High } }));
        Assert.Throws<ArgumentException>(() => new OpenAiClient(http, Options with
        {
            DecisionProfiles = new Dictionary<string, ModelSelection> { ["bad"] = new() { ModelId = "decision", ReasoningEffort = ReasoningEffort.High } },
        }));
    }

    [Fact]
    public async Task MessagesTasksProfilesAndCredentialsAreIsolatedAcrossConcurrentCalls()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var parts = new List<ContentPart> { new TextPart("original"), ImagePart.FromBytes([255, 216, 255]) };
        var messages = new List<StructuredMessage> { new(MessageRole.User, parts) };
        var choices = new List<DecisionOption<string>> { new("first", "first description"), new("second", "second description") };
        var profiles = new Dictionary<string, ModelSelection> { ["routing"] = new() { ModelId = "original-model" } };
        var task = DecisionTask.CreateChoice<string>("original instructions", choices);
        var observed = new List<string>();
        using var handler = new Handler(async message =>
        {
            using var doc = JsonDocument.Parse(await message.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken));
            var root = doc.RootElement;
            var first = root.GetProperty("model").GetString() == "original-model";
            Assert.Equal(first ? "first-key" : "second-key", message.Headers.Authorization!.Parameter);
            var question = root.GetProperty("questions")[0];
            Assert.Equal("original instructions", question.GetProperty("instructions").GetString());
            Assert.Equal("first", question.GetProperty("choices")[0].GetProperty("value").GetString());
            Assert.Equal("first description", question.GetProperty("choices")[0].GetProperty("description").GetString());
            if(first)
            {
                Assert.Equal("original", root.GetProperty("input")[0].GetProperty("content")[0].GetProperty("text").GetString());
                Assert.Equal("input_image", root.GetProperty("input")[0].GetProperty("content")[1].GetProperty("type").GetString());
            }

            return ResponseTestSupport.Response("""
                {"model":"resolved","answers":[{"name":"answer","type":"choice","choice":"first","confidence":0.6,
                  "probabilities":[{"value":"first","probability":0.8},{"value":"second","probability":0.2}]}],"usage":{"input_tokens":4}}
                """);
        });
        using var http = new HttpClient(handler);
        var client = new OpenAiClient(http, Options with { DecisionProfiles = profiles });
        var pending = client.DecideAsync(task, new OpenAiDecisionRequest
        {
            Messages = messages,
            CredentialResolver = _ =>
            {
                entered.SetResult();
                return new(release.Task);
            },
            UsageObserver = (_, _) =>
            {
                observed.Add("first");
                return ValueTask.CompletedTask;
            },
        }, TestContext.Current.CancellationToken);
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        choices[0] = new("mutated");
        parts.Clear();
        messages.Clear();
        profiles["routing"] = new() { ModelId = "mutated-model" };
        var second = await client.DecideAsync(task, new OpenAiDecisionRequest
        {
            Input = "other",
            ModelSelection = new() { ModelId = "second-model" },
            CredentialResolver = Credentials.FromStatic("second-key"),
            UsageObserver = (_, _) =>
            {
                observed.Add("second");
                return ValueTask.CompletedTask;
            },
        }, TestContext.Current.CancellationToken);
        release.SetResult("first-key");
        Assert.Equal("first", (await pending).EnsureSuccess().Choice);
        Assert.Equal("first", second.EnsureSuccess().Choice);
        Assert.Equal(new[] { "second", "first" }, observed);
    }

    [Fact]
    public async Task ScalarPredicateBooleanChoiceAndScoreTasksWork()
    {
        using var handler = new Handler(async message =>
        {
            using var doc = JsonDocument.Parse(await message.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken));
            var question = doc.RootElement.GetProperty("questions")[0];
            var kind = question.GetProperty("type").GetString();
            if(kind == "choice")
            {
                Assert.False(question.GetProperty("choices")[0].GetProperty("value").GetBoolean());
                Assert.True(question.GetProperty("choices")[1].GetProperty("value").GetBoolean());
            }

            return ResponseTestSupport.Response(kind switch
            {
                "predicate" => """{"model":"resolved","answers":[{"name":"answer","type":"predicate","probability":0.9}]}""",
                "choice" => """{"model":"resolved","answers":[{"name":"answer","type":"choice","choice":true,"confidence":0.6,"probabilities":[{"value":false,"probability":0.1},{"value":true,"probability":0.9}]}]}""",
                _ => """{"model":"resolved","answers":[{"name":"answer","type":"score","score":0.8,"confidence":0.6,"probabilities":[{"value":0,"label":"Low","probability":0.2},{"value":1,"label":"High","probability":0.8}]}]}""",
            });
        });
        using var http = new HttpClient(handler);
        var client = new OpenAiClient(http, Options);
        Assert.Equal(0.9, (await client.DecideAsync(DecisionTask.Create<DecisionPredicate>("condition"), "evidence", TestContext.Current.CancellationToken)).EnsureSuccess().Probability);
        Assert.True((await client.DecideAsync(DecisionTask.CreateChoice<bool>("yes or no"), "evidence", TestContext.Current.CancellationToken)).EnsureSuccess().Choice);
        Assert.Equal(0.8, (await client.DecideAsync(DecisionTask.CreateScore("score", [new("Low"), new("High")]), "evidence", TestContext.Current.CancellationToken)).EnsureSuccess().Score);
    }

    [Fact]
    public async Task ExplicitEnumSubsetAndBooleanDescriptionsAreSent()
    {
        var task = DecisionTask.Create<CustomChoices>(q => q
            .Ask(x => x.Department, "choose", choices: [Department.Technical, Department.Billing])
            .Ask(x => x.Accepted, "choose", choices: [new DecisionOption<bool>(true, "Accept"), new(false, "Reject")]));
        using var handler = new Handler(async message =>
        {
            using var doc = JsonDocument.Parse(await message.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken));
            var questions = doc.RootElement.GetProperty("questions");
            Assert.Equal("Technical", questions[0].GetProperty("choices")[0].GetProperty("value").GetString());
            Assert.Equal("Accept", questions[1].GetProperty("choices")[0].GetProperty("description").GetString());
            return ResponseTestSupport.Response("""
                {"model":"resolved","answers":[
                 {"name":"Department","type":"choice","choice":"billing","confidence":0.5,"probabilities":[{"value":"billing","probability":0.8},{"value":"Technical","probability":0.2}]},
                 {"name":"Accepted","type":"choice","choice":true,"confidence":0.5,"probabilities":[{"value":true,"probability":0.8},{"value":false,"probability":0.2}]}]}
                """);
        });
        using var http = new HttpClient(handler);
        var result = await new OpenAiClient(http, Options).DecideAsync(task, "ticket", TestContext.Current.CancellationToken);
        Assert.Equal(Department.Technical, result.EnsureSuccess().Department.Probabilities[0].Value);
        Assert.True(result.Value!.Accepted.Choice);
    }

    [Fact]
    public async Task PreCancelledRequestsDoNotSend()
    {
        using var caller = new CancellationTokenSource();
        caller.Cancel();
        using var handler = new Handler(_ => throw new InvalidOperationException("Should not send"));
        using var http = new HttpClient(handler);
        var exception = await Assert.ThrowsAsync<StructuredOperationCanceledException>(() => new OpenAiClient(http, Options).DecideAsync(TicketTask(), "ticket", caller.Token));
        Assert.Equal(caller.Token, exception.CancellationToken);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task DeadlineBoundsNoncooperativeSendAndDisposesLateResponse()
    {
        var clock = new AuxiliaryClock();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new Handler(_ =>
        {
            entered.SetResult();
            return release.Task;
        });
        using var http = new HttpClient(handler);
        var client = new OpenAiClient(http, Options with { TimeProvider = clock });
        var pending = client.DecideAsync(TicketTask(), new OpenAiDecisionRequest { Input = "ticket", TotalTimeout = TimeSpan.FromSeconds(1) }, TestContext.Current.CancellationToken);
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(StructuredErrorKind.DeadlineExceeded, (await pending).Error!.Kind);
        using var content = new EmbeddingAndImageTestSupport.TrackedContent();
        release.SetResult(new(HttpStatusCode.OK) { Content = content });
        await content.Disposed.Task.WaitAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CallerCancellationDuringUsageRetainsAccountingAndCallbackWarning()
    {
        using var caller = new CancellationTokenSource();
        using var handler = new Handler(_ => Task.FromResult(ResponseTestSupport.Response(Envelope)));
        using var http = new HttpClient(handler);
        var request = new OpenAiDecisionRequest
        {
            Input = "ticket",
            UsageObserver = (usage, token) =>
            {
                Assert.True(usage.Succeeded);
                caller.Cancel();
                Assert.False(token.IsCancellationRequested);
                return ValueTask.FromException(new InvalidOperationException("secret"));
            },
        };
        var exception = await Assert.ThrowsAsync<StructuredOperationCanceledException>(() => new OpenAiClient(http, Options).DecideAsync(TicketTask(), request, caller.Token));
        Assert.Equal(42, exception.Metadata.Usage!.InputTokens);
        Assert.Equal(caller.Token, exception.CancellationToken);
        Assert.Contains(exception.Warnings, x => x.Code == "UsageObserverFailed");
    }

    [Fact]
    public async Task ResponseSizeCeilingAndHttpTaxonomyApply()
    {
        using var handler = new Handler(_ => Task.FromResult(ResponseTestSupport.Response(Envelope)));
        using var http = new HttpClient(handler);
        var limited = await new OpenAiClient(http, Options with { MaxResponseBytes = 10 }).DecideAsync(TicketTask(), "ticket", TestContext.Current.CancellationToken);
        Assert.Equal(StructuredErrorKind.InvalidResponse, limited.Error!.Kind);
        using var failingHandler = new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("""{"error":{"code":"insufficient_quota","message":"secret"},"usage":{"input_tokens":4}}"""),
        }));
        using var failingHttp = new HttpClient(failingHandler);
        var failed = await new OpenAiClient(failingHttp, Options).DecideAsync(TicketTask(), "ticket", TestContext.Current.CancellationToken);
        Assert.Equal(StructuredErrorKind.RateLimited, failed.Error!.Kind);
        Assert.False(failed.Error.IsTransient);
        Assert.DoesNotContain("secret", failed.Error.Message);
        Assert.Equal(4, failed.Metadata.Usage!.InputTokens);
        Assert.Equal(1, failingHandler.Calls);
    }

    sealed record CustomChoices
    {
        public required DecisionChoice<Department> Department { get; init; }
        public required DecisionChoice<bool> Accepted { get; init; }
    }
}
