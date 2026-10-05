namespace Structly.AI.OpenAI;

public sealed partial class OpenAiClient
{
    async Task<StructuredResult<T>> RunOperation<T>(StructuredRequest request, string operation,
        Func<OpenAiExecution, Task<StructuredResult<T>>> core, CancellationToken cancellationToken, string? schemaName = null)
    {
        using var execution = new OpenAiExecution(request, _options, cancellationToken);
        execution.Metadata = execution.Metadata with { Operation = operation, SchemaName = schemaName };
        StructuredResult<T> result;
        try
        {
            result = await core(execution).ConfigureAwait(false);
        }
        catch(OperationCanceledException)
        {
            result = LocalFailure<T>(execution, execution.Deadline ?? StructuredErrorKind.TransportFailure, true);
        }

        if(execution.Deadline is { } deadline)
            result = LocalFailure<T>(execution, deadline, true);

        if(execution.Metadata.Usage is not null && (request.UsageObserver ?? _options.UsageObserver) is { } observer)
        {
            var usage = new StructuredUsageEvent
            {
                Metadata = execution.Metadata,
                Provider = execution.Metadata.Provider!,
                RequestedModel = execution.Metadata.RequestedModel!,
                Usage = execution.Metadata.Usage!,
                Succeeded = result.IsSuccess && !cancellationToken.IsCancellationRequested,
                FailureKind = result.Error?.Kind,
                CallerCancelled = cancellationToken.IsCancellationRequested,
            };
            await execution.Callback(token => observer(usage, token), TimeSpan.FromSeconds(5), "UsageObserver").ConfigureAwait(false);
        }

        if(cancellationToken.IsCancellationRequested)
            throw new StructuredOperationCanceledException(execution.Metadata, execution.Warnings, cancellationToken);

        if(execution.Deadline is { } finalDeadline)
            return LocalFailure<T>(execution, finalDeadline, true);

        return result.IsSuccess ? StructuredResult<T>.Success(result.Value!, execution.Metadata, execution.Warnings)
            : StructuredResult<T>.Failure(result.Error!, execution.Metadata, execution.Warnings);
    }

    static StructuredResult<T> LocalFailure<T>(OpenAiExecution execution, StructuredErrorKind kind, bool transient = false,
        IReadOnlyList<StructuredIssue>? issues = null) => StructuredResult<T>.Failure(new StructuredError
        {
            Kind = kind,
            Message = kind switch
            {
                StructuredErrorKind.DeadlineExceeded => $"Total timeout expired ({execution.TotalTimeout}).",
                StructuredErrorKind.InactivityExceeded => $"Inactivity timeout expired ({execution.InactivityTimeout}).",
                _ => $"Structured operation failed: {kind}.",
            },
            TotalTimeout = kind is StructuredErrorKind.DeadlineExceeded or StructuredErrorKind.InactivityExceeded ? execution.TotalTimeout : null,
            InactivityTimeout = kind is StructuredErrorKind.DeadlineExceeded or StructuredErrorKind.InactivityExceeded ? execution.InactivityTimeout : null,
            IsTransient = transient,
            Issues = issues ?? [],
        },
        execution.Metadata, execution.Warnings);

    static StructuredResult<T> InvalidOptions<T>(OpenAiExecution execution) => LocalFailure<T>(execution, StructuredErrorKind.InvalidRequest,
        issues: [new("$", "RequestOptions", "Check input, model/profile, deadlines and provider options. Output guidance requires a valid ExampleJson or IncludeExample=false when a sample cannot be generated.")]);

}
