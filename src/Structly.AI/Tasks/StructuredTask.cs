namespace Structly.AI;

/// <summary>Creates reusable typed contracts without transport or credentials.</summary>
public static class StructuredTask
{
    /// <summary>Creates a reusable typed contract with task instructions.</summary>
    public static StructuredTask<T> Create<T>(string instructions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instructions);
        return new(new StructuredTaskOptions { Instructions = instructions });
    }

    /// <summary>Validates settings, resolves the DTO and validates its static schema at startup.</summary>
    [System.Runtime.CompilerServices.OverloadResolutionPriority(1)]
    public static StructuredTask<T> Create<T>(StructuredTaskOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new(options);
    }
}
