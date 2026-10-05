namespace Structly.AI;

/// <summary>Explicit credential factories; no ambient fallback.</summary>
public static class Credentials
{
    /// <summary>Uses a fixed credential.</summary>
    public static Func<CancellationToken, ValueTask<string?>> FromStatic(string credential)
    {
        if(String.IsNullOrWhiteSpace(credential))
            throw new ArgumentException("Credential must be nonblank.", nameof(credential));

        return _ => ValueTask.FromResult<string?>(credential);
    }

    /// <summary>Reads the named environment variable on every execution.</summary>
    public static Func<CancellationToken, ValueTask<string?>> FromEnvironment(string variable)
    {
        if(String.IsNullOrWhiteSpace(variable))
            throw new ArgumentException("Variable must be nonblank.", nameof(variable));

        return _ => ValueTask.FromResult(Environment.GetEnvironmentVariable(variable));
    }
}
