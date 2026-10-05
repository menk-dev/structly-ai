using Microsoft.Extensions.Options;
using Structly.AI.OpenAI;

namespace Structly.AI.Hosting;

sealed class OpenAiHostingOptionsValidator : IValidateOptions<OpenAiHostingOptions>
{
    public ValidateOptionsResult Validate(string? name, OpenAiHostingOptions options)
    {
        try
        {
            using var http = new HttpClient();
            _ = new OpenAiClient(http, options.ToClientOptions());
            return ValidateOptionsResult.Success;
        }
        catch(ArgumentException)
        {
            return ValidateOptionsResult.Fail("Invalid Structly OpenAI settings. Check model selections, profiles, cache compatibility, API directory URI, deadlines and response size limits.");
        }
    }
}
