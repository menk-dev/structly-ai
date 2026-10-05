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

    [Fact]
    public void RequestCompletionHandlesArrayObjectsVocabulariesAndConstraints()
    {
        const string request = """
            {"text":{"format":{"type":"json_schema","schema":{"type":"object","properties":{
              "items":{"type":"array","minItems":1,"items":{"type":"object","properties":{
                "name":{"type":"string","enum":["request-value"]},"count":{"type":"integer","minimum":2}}}},
              "optional":{"anyOf":[{"type":"string"},{"type":"null"}]},
              "code":{"type":"string","pattern":"^[A-Z]+$"}
            }}}}}
            """;
        var value = ResponseEnvelopes.CompleteFromRequest(request, "{\"items\":[{\"count\":3}],\"code\":\"OK\"}");
        Assert.Equal("request-value", value.GetProperty("items")[0].GetProperty("name").GetString());
        Assert.Equal(3, value.GetProperty("items")[0].GetProperty("count").GetInt32());
        Assert.Equal(JsonValueKind.Null, value.GetProperty("optional").ValueKind);
        Assert.Equal(2, ResponseEnvelopes.CompleteFromRequest(request, "{\"code\":\"OK\"}").GetProperty("items")[0].GetProperty("count").GetInt32());
        foreach(var partial in new[] { "{}", "{\"code\":\"bad\"}", "{\"code\":null}", "{\"code\":\"OK\",\"unknown\":1}", "{\"code\":\"OK\",\"items\":[]}" })
            Assert.Throws<ArgumentException>(() => ResponseEnvelopes.CompleteFromRequest(request, partial));

        Assert.Throws<ArgumentException>(() => ResponseEnvelopes.CompleteFromRequest("{}", "{}"));
    }

    [Theory]
    [InlineData("{\"type\":\"integer\",\"minimum\":0}", "-1e-30")]
    [InlineData("{\"type\":\"integer\"}", "1.00000000000000001")]
    [InlineData("{\"type\":\"string\",\"format\":\"date\"}", "\"invalid\"")]
    [InlineData("{\"type\":\"string\",\"minLength\":2}", "\"x\"")]
    [InlineData("{\"type\":\"string\",\"maxLength\":1}", "\"xx\"")]
    [InlineData("{\"type\":\"string\",\"enum\":[\"valid\"]}", "\"other\"")]
    [InlineData("{\"type\":\"array\",\"items\":{\"type\":\"boolean\"},\"maxItems\":1}", "[true,false]")]
    public void RequestCompletionRejectsInvalidSuppliedScalarsAndArrays(string schema, string partial)
    {
        var request = "{\"text\":{\"format\":{\"type\":\"json_schema\",\"schema\":" + schema + "}}}";
        Assert.Throws<ArgumentException>(() => ResponseEnvelopes.CompleteFromRequest(request, partial));
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
