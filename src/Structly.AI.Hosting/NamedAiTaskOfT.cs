using Structly.AI.OpenAI;

namespace Structly.AI.Hosting;

sealed class NamedAiTask<T>(StructuredTask<T>? task, BoundOutput<T>? output)
{
    internal Task<StructuredResult<T>> ExecuteAsync(OpenAiClient client, StructuredRequest request, CancellationToken cancellationToken)
        => output is not null ? client.ExecuteAsync(output, request, cancellationToken) : client.ExecuteAsync(task!, request, cancellationToken);
}
