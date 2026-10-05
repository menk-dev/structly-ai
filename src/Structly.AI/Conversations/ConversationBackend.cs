namespace Structly.AI;

abstract class ConversationBackend
{
    internal abstract ConversationConfiguration Resolve(ConversationConfiguration configuration);
    internal abstract ConversationBackend Branch();
    internal abstract Task<StructuredResult<T>> Execute<T>(BoundOutput<T> output, ConversationConfiguration configuration,
        ConversationRequest request, CancellationToken cancellationToken);
}
