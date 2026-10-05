using System.Text.Json;

namespace Structly.AI.Testing;

/// <summary>A detached HTTP request snapshot. Authorization headers are not captured.</summary>
public sealed record CapturedRequest
{
    /// <summary>Gets the HTTP method.</summary>
    public required HttpMethod Method { get; init; }
    /// <summary>Gets the request URI.</summary>
    public required Uri Uri { get; init; }
    /// <summary>Gets the body exactly as sent, or null when absent.</summary>
    public string? Body { get; init; }
    /// <summary>Parses the captured JSON body and returns a detached value.</summary>
    public JsonElement ReadJson()
    {
        using var document = JsonDocument.Parse(Body ?? throw new InvalidOperationException("The request has no body."));
        return document.RootElement.Clone();
    }
}
