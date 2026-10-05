sealed class UsageCounter
{
    int _count;
    public int Count => Volatile.Read(ref _count);
    public void Record() => Interlocked.Increment(ref _count);
}
