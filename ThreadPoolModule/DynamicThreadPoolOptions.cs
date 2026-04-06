namespace ThreadPoolModule;

// Параметры динамического пула (лабораторная работа).
public sealed class DynamicThreadPoolOptions
{
    public int MinThreads { get; init; } = 2;
    public int MaxThreads { get; init; } = 8;

    // Порог длины очереди
    public int QueueLengthScaleThreshold { get; init; } = 3;

    // Порог ожидания (мс) для самой старой задачи в очереди 
    public int OldestTaskWaitThresholdMs { get; init; } = 200;

    // Макс простой потока (мс)
    public int IdleTimeoutMs { get; init; } = 1500;

    // Долгое выполнение одной задачи (мс) 
    public int StuckThreadTimeoutMs { get; init; } = 8000;

    //Как часто фоновый поток проверяет зависшие потоки
    public int WatchdogPeriodMs { get; init; } = 400;
    public Action<string>? LogInfo { get; init; }
    public Action<Exception>? LogError { get; init; }
}
