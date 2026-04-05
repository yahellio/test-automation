namespace ThreadPoolModule;

public sealed class PoolSnapshot
{
    public int ActiveThreads { get; init; }

    public int QueueLength { get; init; }

    public long CompletedTasks { get; init; }

    public long PendingTasks { get; init; }

    public int OldestWaitMs { get; init; }

    public long StuckReplacements { get; init; }

    public long WorkerFaultRecoveries { get; init; }
}
