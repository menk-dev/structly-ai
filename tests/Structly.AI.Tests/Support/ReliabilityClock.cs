namespace Structly.AI.Tests;

// Tests advance monotonic time explicitly; no sleeps or provider credentials.
sealed class ReliabilityClock : TimeProvider
{
    readonly List<ManualTimer> _timers = [];
    readonly object _sync = new();
    long _ticks;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp()
    {
        lock(_sync)
            return _ticks;
    }
    public override DateTimeOffset GetUtcNow() => new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddTicks(GetTimestamp());
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        lock(_sync)
        {
            _timers.Add(timer);
            timer.Change(dueTime, period);
        }

        return timer;
    }
    public void Advance(TimeSpan duration)
    {
        List<ManualTimer> due;
        lock(_sync)
        {
            _ticks += duration.Ticks;
            due = _timers.Where(t => t.Due <= _ticks).ToList();
            foreach(var timer in due)
                timer.Due = Int64.MaxValue;
        }

        foreach(var timer in due)
            timer.Fire();
    }
    sealed class ManualTimer(ReliabilityClock clock, TimerCallback callback, object? state) : ITimer
    {
        public long Due { get; set; } = Int64.MaxValue;
        bool _disposed;
        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock(clock._sync)
            {
                if(_disposed)
                    return false;

                Due = dueTime == Timeout.InfiniteTimeSpan ? Int64.MaxValue : clock._ticks + dueTime.Ticks;
                return true;
            }
        }
        public void Fire()
        {
            lock(clock._sync)
            {
                if(!_disposed)
                    callback(state);
            }
        }
        public void Dispose()
        {
            lock(clock._sync)
            {
                _disposed = true;
                Due = Int64.MaxValue;
            }
        }
        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
