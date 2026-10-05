namespace Structly.AI;

/// <summary>Creates reusable typed contracts without transport or credentials.</summary>
public static class StructuredTask
{
    /// <summary>Validates settings, resolves the DTO and validates its static schema at startup.</summary>
    public static StructuredTask<T> Create<T>(StructuredTaskOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new(options);
    }
}
