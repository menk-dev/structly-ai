using Structly.AI.OpenAI;
using Structly.AI.Testing;
using static Structly.AI.Tests.ReliabilityTestSupport;

namespace Structly.AI.Tests;

public sealed class TaskExecutionDefaultsTests
{
    [Fact]
    public async Task Direct_bound_and_conversation_calls_inherit_caps_without_mutating_requests()
    {
        using var fixture = new OpenAiTestFixture();
        var task = StructuredTask.Create<Answer>(new()
        {
            Instructions = "Extract",
            ExecutionDefaults = new() { MaxOutputTokens = 800, InactivityTimeout = TimeSpan.FromSeconds(3) },
        });
        var other = StructuredTask.Create<Answer>(new()
        {
            Instructions = "Other",
            ExecutionDefaults = new() { MaxOutputTokens = 400 },
        });
        var request = new OpenAiRequest { Input = "input" };
        for(var i = 0; i < 5; i++)
            fixture.Enqueue(ResponseEnvelopes.CompletedText("{\"value\":42}"));

        Assert.True((await fixture.Client.ExecuteAsync(task, request, TestToken)).IsSuccess);
        Assert.True((await fixture.Client.ExecuteAsync(task.BindOutput(), "input", TestToken)).IsSuccess);
        Assert.True((await fixture.Client.ExecuteAsync(task, request with { MaxOutputTokens = 1600 }, TestToken)).IsSuccess);
        var conversation = fixture.Client.CreateConversation(task);
        Assert.True((await conversation.ChangeOutput(other.BindOutput()).ExecuteAsync(new() { Input = "input" }, TestToken)).IsSuccess);
        Assert.True((await conversation.ExecuteAsync(new() { Input = "input", MaxOutputTokens = 2000 }, TestToken)).IsSuccess);
        Assert.Null(request.MaxOutputTokens);
        Assert.Null(request.InactivityTimeout);
        Assert.Equal(new[] { 800, 800, 1600, 800, 2000 }, fixture.Requests.Select(item => item.ReadJson().GetProperty("max_output_tokens").GetInt32()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Task_deadlines_and_request_overrides_bound_execution(bool overrideDefaults)
    {
        var clock = new ReliabilityClock();
        var credential = Gate<string?>();
        using var handler = new Handler(_ => throw new InvalidOperationException("No HTTP expected"));
        using var http = new HttpClient(handler);
        var task = StructuredTask.Create<Answer>(new()
        {
            Instructions = "Extract",
            ExecutionDefaults = new() { TotalTimeout = TimeSpan.FromSeconds(2) },
        });
        var pending = Client(http, clock).ExecuteAsync(task, new()
        {
            Input = "input",
            CredentialResolver = _ => new(credential.Task),
            TotalTimeout = overrideDefaults ? TimeSpan.FromSeconds(4) : null,
        }, TestToken);
        clock.Advance(TimeSpan.FromSeconds(2));
        if(overrideDefaults)
        {
            Assert.False(pending.IsCompleted);
            clock.Advance(TimeSpan.FromSeconds(2));
        }

        var result = await pending.WaitAsync(TestToken);
        Assert.Equal(StructuredErrorKind.DeadlineExceeded, result.Error!.Kind);
        Assert.Equal(TimeSpan.FromSeconds(overrideDefaults ? 4 : 2), result.Error.TotalTimeout);
        Assert.Equal(0, handler.Calls);
        credential.SetResult("late");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Streaming_inherits_task_inactivity_budget(bool conversation)
    {
        var clock = new ReliabilityClock();
        using var stream = new ControlledStream();
        using var handler = new Handler(_ => Task.FromResult(Response(stream)));
        using var http = new HttpClient(handler);
        var client = Client(http, clock);
        var task = StructuredTask.Create<Answer>(new()
        {
            Instructions = "Extract",
            ExecutionDefaults = new() { InactivityTimeout = TimeSpan.FromSeconds(3) },
        });
        var pending = conversation
            ? client.CreateConversation(task).ExecuteAsync(new() { Input = "input", Stream = true }, TestToken)
            : client.ExecuteAsync(task.BindOutput(), new() { Input = "input", Stream = true }, TestToken);
        await stream.NextRead();
        clock.Advance(TimeSpan.FromSeconds(3));
        var result = await pending.WaitAsync(TestToken);
        Assert.Equal(StructuredErrorKind.InactivityExceeded, result.Error!.Kind);
        Assert.Equal(TimeSpan.FromSeconds(3), result.Error.InactivityTimeout);
    }

    [Fact]
    public async Task Invalid_request_overrides_do_not_fall_back_to_defaults()
    {
        using var fixture = new OpenAiTestFixture();
        var task = StructuredTask.Create<Answer>(new()
        {
            Instructions = "Extract",
            ExecutionDefaults = new() { MaxOutputTokens = 800, TotalTimeout = TimeSpan.FromSeconds(2) },
        });
        foreach(var request in new[]
        {
            new OpenAiRequest { Input = "input", MaxOutputTokens = 0 },
            new OpenAiRequest { Input = "input", TotalTimeout = TimeSpan.Zero },
            new OpenAiRequest { Input = "input", InactivityTimeout = TimeSpan.FromSeconds(1) },
        })
            Assert.Equal(StructuredErrorKind.InvalidRequest, (await fixture.Client.ExecuteAsync(task, request, TestToken)).Error!.Kind);

        Assert.Empty(fixture.Requests);
    }

    [Fact]
    public void Invalid_defaults_fail_when_creating_tasks()
    {
        foreach(var defaults in new[]
        {
            new TaskExecutionDefaults { MaxOutputTokens = 0 },
            new TaskExecutionDefaults { TotalTimeout = TimeSpan.Zero },
            new TaskExecutionDefaults { TotalTimeout = TimeSpan.FromHours(25) },
            new TaskExecutionDefaults { InactivityTimeout = TimeSpan.Zero },
        })
            Assert.Throws<ArgumentException>(() => StructuredTask.Create<Answer>(new() { ExecutionDefaults = defaults }));

        Assert.Throws<ArgumentNullException>(() => StructuredTask.Create<Answer>(new() { ExecutionDefaults = null! }));
    }
}
