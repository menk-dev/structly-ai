namespace Structly.AI.Tests;

public sealed class StructuredResultTests
{
    [Fact]
    public void Success_branches_and_try_get_value_expose_nonnull_values()
    {
        var metadata = new StructuredMetadata();
        var result = StructuredResult<string>.Success("answer", metadata);
        if(result.IsSuccess)
            Assert.Equal(6, result.Value.Length);
        else
            Assert.Fail(result.Error.Message);

        Assert.True(result.TryGetValue(out var value));
        if(result.TryGetValue(out var nonnull))
            Assert.Equal(6, nonnull.Length);

        Assert.Equal("answer", value);
        Assert.Same(metadata, result.Metadata);
    }

    [Fact]
    public void Failure_and_default_value_success_remain_distinct()
    {
        var metadata = new StructuredMetadata();
        var error = new StructuredError { Kind = StructuredErrorKind.InvalidOutput, Message = "Invalid output." };
        var failure = StructuredResult<string>.Failure(error, metadata);
        Assert.False(failure.TryGetValue(out var value));
        Assert.Null(value);
        if(!failure.IsSuccess)
            Assert.Equal("Invalid output.", failure.Error.Message);

        if(!failure.TryGetValue(out _))
            Assert.Equal("Invalid output.", failure.Error.Message);

        var nullable = StructuredResult<string?>.Success("answer", metadata);
        if(nullable.TryGetValue(out var nonnull))
            Assert.Equal(6, nonnull.Length);

        Assert.Same(metadata, failure.Metadata);
        var success = StructuredResult<int>.Success(0, metadata);
        Assert.True(success.TryGetValue(out var zero));
        Assert.Equal(0, zero);
        Assert.False(StructuredResult<int>.Failure(error, metadata).TryGetValue(out _));
    }
}
