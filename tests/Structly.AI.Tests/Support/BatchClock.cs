namespace Structly.AI.Tests;

sealed class BatchClock : TimeProvider
{
    long _ticks;
    readonly List<Timer> _timers = [];
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => _ticks;
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new Timer(this, callback, state);
        timer.Change(dueTime, period);
        _timers.Add(timer);
        return timer;
    }
    public void Advance(TimeSpan duration)
    {
        _ticks += duration.Ticks;
        foreach(var timer in _timers.ToArray())
            timer.Fire();
    }
    sealed class Timer(BatchClock clock, TimerCallback callback, object? state) : ITimer
    {
        long _due = Int64.MaxValue;
        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            _due = dueTime == Timeout.InfiniteTimeSpan ? Int64.MaxValue : clock._ticks + dueTime.Ticks;
            return true;
        }
        public void Fire()
        {
            if(clock._ticks < _due)
                return;

            _due = Int64.MaxValue;
            callback(state);
        }
        public void Dispose() => _due = Int64.MaxValue;
        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
