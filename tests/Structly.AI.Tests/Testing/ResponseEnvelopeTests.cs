using Structly.AI.Testing;
using System.Text.Json;
using static Structly.AI.Tests.BatchTestSupport;

namespace Structly.AI.Tests;

public sealed class ResponseEnvelopeTests
{
    [Fact]
    public async Task TestingBuildersPreserveInvalidJsonAndExplicitCompletion()
    {
        var task = StructuredTask.Create<Answer>(new());
        Assert.True(task.ReadOutput(task.CreateExample().GetRawText()).IsSuccess);
        var completed = ResponseEnvelopes.CompleteExample(task, "{}");
        Assert.True(task.ReadOutput(completed.GetRawText()).IsSuccess);
        Assert.Equal("supplied", ResponseEnvelopes.CompleteExample(task, "{\"value\":\"supplied\"}").GetProperty("value").GetString());
        Assert.Throws<ArgumentException>(() => ResponseEnvelopes.CompleteExample(task, "{\"value\":null}"));
        Assert.Throws<ArgumentException>(() => ResponseEnvelopes.CompleteExample(task, "{\"unknown\":true}"));
        using var handler = new Handler(_ => ResponseEnvelopes.ToHttpResponse(ResponseEnvelopes.CompletedText("{broken")));
        using var http = new HttpClient(handler);
        var result = await Client(http).ExecuteAsync(task, new() { Input = "input", Instructions = "Extract" }, TestContext.Current.CancellationToken);
        Assert.Equal(StructuredErrorKind.InvalidOutput, result.Error!.Kind);
    }

    [Theory]
    [InlineData(false, StructuredErrorKind.Refused)]
    [InlineData(true, StructuredErrorKind.IncompleteOutput)]
    public async Task TestingRefusalAndIncompleteEnvelopesUseRealResponseProcessing(bool incomplete, StructuredErrorKind expected)
    {
        using var handler = new Handler(_ => ResponseEnvelopes.ToHttpResponse(incomplete ? ResponseEnvelopes.Incomplete("{partial") : ResponseEnvelopes.Refusal()));
        using var http = new HttpClient(handler);
        var task = StructuredTask.Create<Answer>(new() { Instructions = "Extract" });
        Assert.Equal(expected, (await Client(http).ExecuteAsync(task, new() { Input = "input" }, TestContext.Current.CancellationToken)).Error!.Kind);
    }

    [Fact]
    public void CompletionPreservesArraysNullsAndNestedSuppliedValues()
    {
        var task = StructuredTask.Create<NestedAnswer>(new());
        var result = ResponseEnvelopes.CompleteExample(task, "{\"child\":{\"value\":\"keep\"},\"items\":[\"first\"],\"optional\":null}");
        Assert.Equal("keep", result.GetProperty("child").GetProperty("value").GetString());
        Assert.Equal("first", result.GetProperty("items")[0].GetString());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("optional").ValueKind);
        Assert.Throws<ArgumentException>(() => StructuredTask.Create<PatternAnswer>(new()).CreateExample());
    }

}
