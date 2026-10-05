using System.Net;

namespace Structly.AI.Tests;

public sealed class StructuredErrorTests
{
    [Theory]
    [InlineData(StructuredErrorKind.InvalidRequest, StructuredErrorCategory.Rejected)]
    [InlineData(StructuredErrorKind.UnsupportedSchema, StructuredErrorCategory.Rejected)]
    [InlineData(StructuredErrorKind.Authentication, StructuredErrorCategory.Unavailable)]
    [InlineData(StructuredErrorKind.PermissionDenied, StructuredErrorCategory.Rejected)]
    [InlineData(StructuredErrorKind.RateLimited, StructuredErrorCategory.Unavailable)]
    [InlineData(StructuredErrorKind.ProviderUnavailable, StructuredErrorCategory.Unavailable)]
    [InlineData(StructuredErrorKind.ProviderRejected, StructuredErrorCategory.Rejected)]
    [InlineData(StructuredErrorKind.TransportFailure, StructuredErrorCategory.Unavailable)]
    [InlineData(StructuredErrorKind.DeadlineExceeded, StructuredErrorCategory.Unavailable)]
    [InlineData(StructuredErrorKind.InactivityExceeded, StructuredErrorCategory.Unavailable)]
    [InlineData(StructuredErrorKind.Refused, StructuredErrorCategory.Rejected)]
    [InlineData(StructuredErrorKind.IncompleteOutput, StructuredErrorCategory.InvalidOutput)]
    [InlineData(StructuredErrorKind.InvalidResponse, StructuredErrorCategory.InvalidOutput)]
    [InlineData(StructuredErrorKind.InvalidOutput, StructuredErrorCategory.InvalidOutput)]
    [InlineData(StructuredErrorKind.CredentialsMissing, StructuredErrorCategory.Unavailable)]
    [InlineData(StructuredErrorKind.BatchItemFailed, StructuredErrorCategory.InvalidOutput)]
    public void CategoriesAreIndependentOfTransientFlag(StructuredErrorKind kind, StructuredErrorCategory category)
    {
        var error = new StructuredError { Kind = kind, Message = "safe" };
        Assert.Equal(category, error.Category);
        Assert.Equal(category, (error with { IsTransient = true }).Category);
    }

    [Fact]
    public void SummaryExcludesArbitraryMessagesAndIncludesStatusAndBudgets()
    {
        var error = new StructuredError
        {
            Kind = StructuredErrorKind.InactivityExceeded,
            Message = "sensitive\nprovider text",
            HttpStatusCode = HttpStatusCode.RequestTimeout,
            TotalTimeout = TimeSpan.FromSeconds(10),
            InactivityTimeout = TimeSpan.FromSeconds(2),
        };
        Assert.Equal("The inactivity timeout expired. HTTP 408. Total timeout: 00:00:10. Inactivity timeout: 00:00:02.", error.Summary);
    }

    [Theory]
    [InlineData(StructuredErrorKind.CredentialsMissing, true)]
    [InlineData(StructuredErrorKind.InvalidOutput, false)]
    public void ExceptionMappingRetainsContext(StructuredErrorKind kind, bool unavailable)
    {
        var metadata = new StructuredMetadata();
        var error = new StructuredError { Kind = kind, Message = "safe" };
        var result = StructuredResult<string>.Failure(error, metadata);
        Exception Map(StructuredOperationException failure, bool expected)
        {
            Assert.Equal(unavailable, expected);
            Assert.Same(error, failure.Error);
            Assert.Same(metadata, failure.Metadata);
            return new ApplicationException(failure.Error.Summary, failure);
        }
        Assert.Throws<ApplicationException>(() => result.EnsureSuccess(e => Map(e, true), e => Map(e, false)));
        Assert.Equal("ok", StructuredResult<string>.Success("ok", metadata).EnsureSuccess(_ => throw new InvalidOperationException(), _ => throw new InvalidOperationException()));
    }
}
