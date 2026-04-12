namespace ThreadPoolModule;

//для старта/остановки воркера
public sealed class PoolWorkerEventArgs : EventArgs
{
    public int WorkerId { get; init; }
    public int ActiveWorkers { get; init; }
}

//при добавлении задачи
public sealed class PoolQueueEventArgs : EventArgs
{
    public int QueueLength { get; init; }
}

//после выполнения одной задачи
public sealed class PoolTaskCompletedEventArgs : EventArgs
{
    public long CompletedTotal { get; init; }
}
