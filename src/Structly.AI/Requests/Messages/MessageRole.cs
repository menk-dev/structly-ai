namespace Structly.AI;

/// <summary>Supported conversation message roles.</summary>
public enum MessageRole
{
    /// <summary>System instructions.</summary>
    System,
    /// <summary>Developer instructions.</summary>
    Developer,
    /// <summary>User input.</summary>
    User,
    /// <summary>Earlier assistant output.</summary>
    Assistant,
}
