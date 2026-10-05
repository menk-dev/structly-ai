namespace Structly.AI;

/// <summary>Validates provider settings and constructs clients without executing requests.</summary>
public interface IStructuredClientFactory<TClient, TOptions> where TClient : class, IStructuredClient where TOptions : class
{
    /// <summary>Validates settings without resolving credentials.</summary>
    void Validate(TOptions options);
    /// <summary>Creates a client with a settings snapshot.</summary>
    TClient Create(HttpClient http, TOptions options);
}
