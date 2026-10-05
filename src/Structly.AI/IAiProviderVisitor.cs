namespace Structly.AI;

/// <summary>Dispatches a selected provider with its concrete types.</summary>
public interface IAiProviderVisitor
{
    /// <summary>Registers the selected concrete provider types.</summary>
    void Visit<TClient, TOptions>(AiProviderDescriptor<TClient, TOptions> descriptor, Action<TOptions>? configure)
        where TClient : class, IStructuredClient where TOptions : class, new();
}
