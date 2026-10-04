namespace Structly.AI.OpenAI;

// Each execution owns its clocks, cancellation sources, metadata and delegates.
sealed class OpenAiExecution : IDisposable
{
    readonly TimeProvider _clock;
    readonly CancellationTokenSource _total;
    readonly CancellationTokenSource _idle = new();
    readonly CancellationTokenSource _operation;
    readonly long _started;
    readonly TimeSpan _budget;
    readonly Func<StructuredProgress, CancellationToken, ValueTask>? _progress;
    ITimer? _idleTimer;
    bool _progressDisabled;

    public OpenAiExecution(StructuredRequest request, OpenAiClientOptions options, CancellationToken caller)
    {
        _clock = options.TimeProvider;
        _started = _clock.GetTimestamp();
        _budget = request.TotalTimeout is { } budget && ValidTimeout(budget) ? budget : options.TotalTimeout;
        // Invalid per-call budgets are classified during preflight, before invoking host code.
        _total = new(_budget, _clock);
        _operation = CancellationTokenSource.CreateLinkedTokenSource(caller, _total.Token, _idle.Token);
        _progress = request.Progress;
        Metadata = new() { Operation = "Structured", Provider = "openai", CorrelationId = request.CorrelationId };
    }

    public StructuredMetadata Metadata { get; set; }
    public List<StructuredWarning> Warnings { get; } = [];
    public CancellationToken Token => _operation.Token;
    public TimeSpan TotalTimeout => _budget;
    public TimeSpan? InactivityTimeout { get; private set; }
    public TimeSpan Remaining => _budget - _clock.GetElapsedTime(_started);
    public StructuredErrorKind? Deadline => _total.IsCancellationRequested || Remaining <= TimeSpan.Zero
        ? StructuredErrorKind.DeadlineExceeded : _idle.IsCancellationRequested ? StructuredErrorKind.InactivityExceeded : null;
    public static bool ValidTimeout(TimeSpan value) => value > TimeSpan.Zero && value <= TimeSpan.FromHours(24);

    void CancelIdle()
    {
        try { _idle.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    public void CheckCancellation()
    {
        if (Remaining <= TimeSpan.Zero) _total.Cancel();
        Token.ThrowIfCancellationRequested();
    }

    // Larger idle windows cannot fire before the maximum total budget. Clamp only the
    // underlying timer interval, so valid TimeSpan values do not exceed timer limits.
    static TimeSpan IdleTimerDelay(TimeSpan duration) => duration > TimeSpan.FromHours(24) ? TimeSpan.FromHours(24) : duration;

    public void StartInactivity(TimeSpan? timeout)
    {
        InactivityTimeout = timeout;
        if (timeout is { } duration)
            _idleTimer = _clock.CreateTimer(_ => CancelIdle(), null, IdleTimerDelay(duration), Timeout.InfiniteTimeSpan);
    }

    public void ResetInactivity(TimeSpan? timeout)
    {
        if (timeout is { } duration) _idleTimer?.Change(IdleTimerDelay(duration), Timeout.InfiniteTimeSpan);
    }

    public void StopInactivity() => _idleTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

    public async Task Await(Task pending)
    {
        try { await pending.WaitAsync(Token).ConfigureAwait(false); }
        catch { _ = ObserveLate(pending); throw; }
    }

    public async Task<T> Await<T>(Task<T> pending, Action<T>? disposeLate = null)
    {
        try { return await pending.WaitAsync(Token).ConfigureAwait(false); }
        catch
        {
            // WaitAsync can abandon a noncooperative operation. Observe its eventual fault,
            // and release a response/stream that arrives after ownership has been abandoned.
            _ = ObserveLate(pending, disposeLate);
            throw;
        }
    }

    static async Task ObserveLate<T>(Task<T> pending, Action<T>? disposeLate)
    {
        try { var value = await pending.ConfigureAwait(false); disposeLate?.Invoke(value); }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException) { }
    }

    static async Task ObserveLate(Task pending)
    {
        try { await pending.ConfigureAwait(false); }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException) { }
    }

    public async ValueTask Progress(StructuredProgress item)
    {
        if (_progress is null || _progressDisabled) return;
        _progressDisabled = !await Callback(token => _progress(item, token), TimeSpan.FromSeconds(1), "ProgressObserver").ConfigureAwait(false);
    }

    public async ValueTask<bool> Callback(Func<CancellationToken, ValueTask> callback, TimeSpan cap, string code)
    {
        if (Remaining <= TimeSpan.Zero)
        {
            Warnings.Add(new(code + "Skipped", "Observer skipped because no execution budget remains."));
            return false;
        }
        using var timeout = new CancellationTokenSource(cap, _clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, _total.Token);
        Task? pending = null;
        try
        {
            pending = callback(linked.Token).AsTask();
            await pending.WaitAsync(linked.Token).ConfigureAwait(false);
            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            if (pending is not null) _ = ObserveLate(pending);
            Warnings.Add(new(code + (linked.IsCancellationRequested ? "TimedOut" : "Failed"), "Observer did not complete successfully."));
            return false;
        }
    }

    public void Dispose()
    {
        _idleTimer?.Dispose();
        _operation.Dispose();
        _idle.Dispose();
        _total.Dispose();
    }
}
