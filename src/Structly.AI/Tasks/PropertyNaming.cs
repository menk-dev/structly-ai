namespace Structly.AI;

/// <summary>The supported serialized property naming policies.</summary>
public enum PropertyNaming
{
    /// <summary>Use System.Text.Json camel case.</summary>
    CamelCase,
    /// <summary>Preserve CLR member names.</summary>
    Preserve,
}
