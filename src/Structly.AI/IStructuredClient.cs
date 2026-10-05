namespace Structly.AI;

/// <summary>Executes structured tasks through a provider.</summary>
public interface IStructuredClient
{
    /// <summary>Executes the supplied output contract.</summary>
    Task<StructuredResult<T>> ExecuteAsync<T>(StructuredTask<T> task, StructuredRequest request, CancellationToken cancellationToken = default);
    /// <summary>Executes the supplied output contract.</summary>
    Task<StructuredResult<T>> ExecuteAsync<T>(BoundOutput<T> output, StructuredRequest request, CancellationToken cancellationToken = default);
}
