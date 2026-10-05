using System.Text;

namespace Structly.AI.OpenAI;

/// <summary>Prepared immutable JSONL and manifest snapshots for submission and persistence.</summary>
public sealed class PreparedBatch
{
    readonly byte[] _jsonl;
    readonly string _manifest;
    internal PreparedBatch(byte[] jsonl, BatchManifest manifest)
    {
        _jsonl = jsonl;
        _manifest = manifest.ToJson();
    }
    /// <summary>Gets a fresh manifest snapshot; caller mutation does not affect preparation.</summary>
    public BatchManifest Manifest => BatchManifest.FromJson(_manifest);
    /// <summary>Gets the complete UTF-8 JSONL as text.</summary>
    public string Jsonl => Encoding.UTF8.GetString(_jsonl);
    /// <summary>Writes prepared UTF-8 JSONL and leaves the destination stream open.</summary>
    public ValueTask WriteJsonlAsync(Stream destination, CancellationToken cancellationToken = default) => destination.WriteAsync(_jsonl, cancellationToken);
}
