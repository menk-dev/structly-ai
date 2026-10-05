namespace Structly.AI;

/// <summary>Explicit model or named profile selection.</summary>
public sealed record ModelSelection
{
    /// <summary>Gets an explicit provider model ID.</summary>
    public string? ModelId { get; init; }
    /// <summary>Gets a consumer-configured profile name.</summary>
    public string? ProfileName { get; init; }
    /// <summary>Gets optional reasoning effort.</summary>
    public ReasoningEffort? ReasoningEffort { get; init; }

    internal void Validate()
    {
        if((ModelId is null) == (ProfileName is null) ||
            ModelId is not null && String.IsNullOrWhiteSpace(ModelId) ||
            ProfileName is not null && String.IsNullOrWhiteSpace(ProfileName) ||
            ReasoningEffort is { } effort && !Enum.IsDefined(effort))
            throw new ArgumentException("Select exactly one nonblank model ID or profile and a supported effort.");
    }
}
