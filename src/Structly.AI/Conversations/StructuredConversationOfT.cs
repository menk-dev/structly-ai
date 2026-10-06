namespace Structly.AI;

/// <summary>A typed conversation with explicit output and configuration transitions.
/// Conversations retain responses at the provider. Failed turns can still be stored or billed.
/// Each branch advances only on success; unavailable retained history is not retried or restarted.</summary>
public sealed class StructuredConversation<T>
{
    readonly ConversationBackend _backend;
    int _active;

    /// <summary>Creates a conversation backed by a provider.</summary>
    public StructuredConversation(BoundOutput<T> output, ConversationConfiguration configuration, ConversationBackend backend)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(backend);
        Output = output;
        Configuration = configuration;
        _backend = backend;
    }

    /// <summary>Gets the immutable bound output.</summary>
    public BoundOutput<T> Output { get; }
    /// <summary>Gets the immutable resolved configuration.</summary>
    public ConversationConfiguration Configuration { get; }

    /// <summary>Executes new user input and advances this branch only on success.</summary>
    public async Task<StructuredResult<T>> ExecuteAsync(ConversationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Enter();
        try
        {
            return await _backend.Execute(Output, Configuration, request, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref _active, 0);
        }
    }

    /// <summary>Creates an independent branch with new output, preserving configuration and credentials.</summary>
    public StructuredConversation<TNext> ChangeOutput<TNext>(BoundOutput<TNext> output)
    {
        ArgumentNullException.ThrowIfNull(output);
        Enter();
        try
        {
            return new(output, Configuration, _backend.Branch());
        }
        finally
        {
            Volatile.Write(ref _active, 0);
        }
    }

    /// <summary>Creates an independent branch with explicitly resolved replacement configuration.</summary>
    public StructuredConversation<T> ChangeConfiguration(ConversationConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        Enter();
        try
        {
            return new(Output, _backend.Resolve(configuration), _backend.Branch());
        }
        finally
        {
            Volatile.Write(ref _active, 0);
        }
    }

    void Enter()
    {
        if(Interlocked.CompareExchange(ref _active, 1, 0) != 0)
            throw new InvalidOperationException("A conversation operation is already active.");
    }
}
