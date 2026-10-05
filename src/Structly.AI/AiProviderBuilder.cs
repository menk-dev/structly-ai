namespace Structly.AI;

/// <summary>Selects exactly one provider for a registration.</summary>
public class AiProviderBuilder
{
    Action<IAiProviderVisitor>? _registration;
    bool _frozen;
    /// <summary>Selects a provider and defers its options callback.</summary>
    public void ConfigureProvider<TClient, TOptions>(AiProviderDescriptor<TClient, TOptions> descriptor, Action<TOptions>? configure = null)
        where TClient : class, IStructuredClient where TOptions : class, new()
    {
        if(_frozen)
            throw new InvalidOperationException("Provider configuration has completed.");

        if(_registration is not null)
            throw new InvalidOperationException("A provider is already selected.");

        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.SectionName);
        ArgumentNullException.ThrowIfNull(descriptor.Factory);
        _registration = visitor => visitor.Visit(descriptor, configure);
    }
    /// <summary>Prevents further provider selection.</summary>
    public void FreezeProvider() => _frozen = true;
    /// <summary>Dispatches the selected provider to a registration visitor.</summary>
    public void VisitProvider(IAiProviderVisitor visitor)
    {
        if(!_frozen)
            throw new InvalidOperationException("Provider configuration has not completed.");

        (_registration ?? throw new InvalidOperationException("Select exactly one provider."))(visitor);
    }
}
