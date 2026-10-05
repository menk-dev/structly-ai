using System.Text.Json;

namespace Structly.AI.OpenAI;

/// <summary>Versioned persistence data for batch correlation and schema validation.</summary>
public sealed record BatchManifest
{
    /// <summary>Gets the manifest format version; currently 1.</summary>
    public int Version { get; init; } = 1;
    /// <summary>Gets the batch endpoint, /v1/embeddings or /v1/responses.</summary>
    public required string Endpoint { get; init; }
    /// <summary>Gets items in original input order.</summary>
    public required IReadOnlyList<BatchManifestItem> Items { get; init; }
    /// <summary>Serializes manifest persistence data without credentials or delegates.</summary>
    public string ToJson() => JsonSerializer.Serialize(this);
    /// <summary>Deserializes a manifest; imports validate its version and contract before HTTP execution.</summary>
    public static BatchManifest FromJson(string json) => JsonSerializer.Deserialize<BatchManifest>(json) ?? throw new ArgumentException("Manifest is required.", nameof(json));
}
