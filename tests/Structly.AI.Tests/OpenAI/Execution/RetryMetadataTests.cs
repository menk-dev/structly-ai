using System.Net;
using System.Net.Http.Headers;
using static Structly.AI.Tests.ReliabilityTestSupport;

namespace Structly.AI.Tests;

public sealed class RetryMetadataTests
{
    [Fact]
    public async Task RetryDateUsesConfiguredClockAndStillMakesOneAttempt()
    {
        var clock = new ReliabilityClock();
        using var handler = new Handler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent("{}") };
            response.Headers.RetryAfter = new RetryConditionHeaderValue(clock.GetUtcNow().AddSeconds(8));
            return Task.FromResult(response);
        });
        using var http = new HttpClient(handler);
        var result = await Client(http, clock).ExecuteAsync(Contract(), new() { Input = "input" }, TestToken);
        Assert.Equal(TimeSpan.FromSeconds(8), result.Error!.RetryAfter);
        Assert.Equal(1, handler.Calls);
    }

}
