namespace Structly.AI;

/// <summary>Provides provider operations for structured conversations.</summary>
public abstract class ConversationBackend
{
    /// <summary>Resolves provider configuration for a conversation.</summary>
    public abstract ConversationConfiguration Resolve(ConversationConfiguration configuration);
    /// <summary>Creates an independent branch of provider state.</summary>
    public abstract ConversationBackend Branch();
    /// <summary>Executes a conversation turn.</summary>
    public abstract Task<StructuredResult<T>> Execute<T>(BoundOutput<T> output, ConversationConfiguration configuration,
        ConversationRequest request, CancellationToken cancellationToken);
}
