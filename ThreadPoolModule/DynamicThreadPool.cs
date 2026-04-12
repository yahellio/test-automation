using System.Runtime.CompilerServices;

namespace ThreadPoolModule;

public sealed class DynamicThreadPool : IDisposable
{
    // События жизненного цикла пула 
    public event EventHandler? PoolInitialized;

    public event EventHandler<PoolWorkerEventArgs>? WorkerThreadStarted;
    public event EventHandler<PoolWorkerEventArgs>? WorkerThreadStopped;
    public event EventHandler<PoolQueueEventArgs>? TaskEnqueued;
    public event EventHandler<PoolTaskCompletedEventArgs>? TaskCompleted;
    public event EventHandler? PoolDisposed;

    private readonly DynamicThreadPoolOptions _options;
    private readonly Queue<WorkItem> _queue = new();
    private readonly object _queueLock = new();
    private readonly Semaphore _workSignal;
    // Ожидание завершения всех задач в пуле
    private readonly ManualResetEventSlim _drained = new(true);
    private readonly List<WorkerContext> _workerContexts = new();
    private readonly object _workersLock = new();
    private readonly Thread _watchdogThread;

    private long _pending;
    private long _completed;
    private long _stuckReplacements;
    private long _workerFaultRecoveries;
    private volatile bool _disposed;

    public DynamicThreadPool(DynamicThreadPoolOptions options)
    {
        _options = options;
        _workSignal = new Semaphore(0, int.MaxValue);

        _options.AfterConstruction?.Invoke(this);

        lock (_workersLock)
        {
            for (var i = 0; i < options.MinThreads; i++)
            {
                StartWorkerUnlocked();
            }
        }

        _watchdogThread = new Thread(WatchdogLoop)
        {
            IsBackground = true,
            Name = "Pool-Watchdog"
        };
        _watchdogThread.Start();

        Raise(PoolInitialized);
    }

    //Поставить задачу в пул
    public void Enqueue(Action work)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var item = new WorkItem(work, DateTime.UtcNow);
        EnterQueueLock();
        try
        {
            Interlocked.Increment(ref _pending);
            _drained.Reset();
            _queue.Enqueue(item);
        }
        finally
        {
            ExitQueueLock();
        }

        _workSignal.Release();

        var qLen = GetQueueLengthLocked();
        Raise(TaskEnqueued, new PoolQueueEventArgs { QueueLength = qLen });

        TryScaleUp();
    }

    // блокирует вызывающий поток, пока все поставленные задачи не будут завершены
    public void WaitForDrain(TimeSpan? timeout = null)
    {
        if (timeout.HasValue)
        {
            _drained.Wait(timeout.Value);
        }
        else
        {
            _drained.Wait();
        }
    }

    public PoolSnapshot GetSnapshot()
    {
        int queueLen;
        int oldestMs;
        EnterQueueLock();
        try
        {
            queueLen = _queue.Count;
            oldestMs = queueLen == 0
                ? 0
                : (int)Math.Min(int.MaxValue, (DateTime.UtcNow - _queue.Peek().EnqueuedUtc).TotalMilliseconds);
        }
        finally
        {
            ExitQueueLock();
        }

        int active;
        lock (_workersLock)
        {
            active = _workerContexts.Count;
        }

        return new PoolSnapshot
        {
            ActiveThreads = active,
            QueueLength = queueLen,
            CompletedTasks = Interlocked.Read(ref _completed),
            PendingTasks = Interlocked.Read(ref _pending),
            OldestWaitMs = oldestMs,
            StuckReplacements = Interlocked.Read(ref _stuckReplacements),
            WorkerFaultRecoveries = Interlocked.Read(ref _workerFaultRecoveries)
        };
    }

    private void TryScaleUp()
    {
        int queueLen;
        int oldestWaitMs;
        EnterQueueLock();
        try
        {
            queueLen = _queue.Count;
            oldestWaitMs = queueLen == 0
                ? 0
                : (int)Math.Min(int.MaxValue, (DateTime.UtcNow - _queue.Peek().EnqueuedUtc).TotalMilliseconds);
        }
        finally
        {
            ExitQueueLock();
        }

        lock (_workersLock)
        {
            if (_workerContexts.Count >= _options.MaxThreads)
            {
                return;
            }

            var needByQueue = queueLen >= _options.QueueLengthScaleThreshold;
            var needByWait = oldestWaitMs >= _options.OldestTaskWaitThresholdMs;
            if (needByQueue || needByWait)
            {
                StartWorkerUnlocked();
            }
        }
    }

    //создаёт замену для зависшего потока
    private void TryAddReplacementWorker()
    {
        lock (_workersLock)
        {
            if (_disposed || _workerContexts.Count >= _options.MaxThreads)
            {
                return;
            }

            Interlocked.Increment(ref _stuckReplacements);
            StartWorkerUnlocked();
        }
    }

    //создаёт и запускает рабочий поток
    private void StartWorkerUnlocked()
    {
        var ctx = new WorkerContext();
        var thread = new Thread(() => WorkerLoop(ctx))
        {
            IsBackground = true,
            Name = $"DynamicPool-{ctx.Id}"
        };
        ctx.WorkerThread = thread;
        _workerContexts.Add(ctx);
        thread.Start();

        Raise(WorkerThreadStarted, new PoolWorkerEventArgs
        {
            WorkerId = ctx.Id,
            ActiveWorkers = _workerContexts.Count
        });
    }

    private void WorkerLoop(WorkerContext ctx)
    {
        try
        {
            while (!_disposed)
            {
                if (!_workSignal.WaitOne(_options.IdleTimeoutMs))
                {
                    //можно ли завершиться проверка
                    var shouldShrink = false;
                    EnterQueueLock();
                    try
                    {
                        if (_disposed)
                        {
                            break;
                        }

                        if (_queue.Count == 0)
                        {
                            lock (_workersLock)
                            {
                                shouldShrink = _workerContexts.Count > _options.MinThreads;
                            }
                        }
                    }
                    finally
                    {
                        ExitQueueLock();
                    }

                    if (shouldShrink)
                    {
                        break;
                    }

                    continue;
                }

                if (_disposed)
                {
                    break;
                }

                //забираем задачу из очереди
                WorkItem? item;
                EnterQueueLock();
                try
                {
                    if (!_queue.TryDequeue(out item))
                    {
                        continue;
                    }
                }
                finally
                {
                    ExitQueueLock();
                }

                //выполненение задачи
                ctx.Busy = true;
                ctx.WorkStartUtc = DateTime.UtcNow;
                try
                {
                    item!.Action.Invoke();
                }
                catch (Exception ex)
                {
                    _options.LogError?.Invoke(ex);
                }
                finally
                {
                    ctx.Busy = false;
                    Interlocked.Decrement(ref _pending);
                    var done = Interlocked.Increment(ref _completed);
                    Raise(TaskCompleted, new PoolTaskCompletedEventArgs { CompletedTotal = done });
                    TrySignalDrained();
                }
            }
        }
        catch (Exception ex)
        {
            _options.LogError?.Invoke(ex);
            Interlocked.Increment(ref _workerFaultRecoveries);
        }
        finally
        {
            RemoveWorkerContext(ctx);
            EnsureMinimumWorkers();
        }
    }

    private void RemoveWorkerContext(WorkerContext ctx)
    {
        int active;
        lock (_workersLock)
        {
            _workerContexts.Remove(ctx);
            active = _workerContexts.Count;
        }

        Raise(WorkerThreadStopped, new PoolWorkerEventArgs
        {
            WorkerId = ctx.Id,
            ActiveWorkers = active
        });
    }

    private void EnsureMinimumWorkers()
    {
        lock (_workersLock)
        {
            if (_disposed)
            {
                return;
            }

            while (_workerContexts.Count < _options.MinThreads)
            {
                StartWorkerUnlocked();
            }
        }
    }

    private void TrySignalDrained()
    {
        if (Interlocked.Read(ref _pending) == 0)
        {
            _drained.Set();
        }
    }

    private void WatchdogLoop()
    {
        while (!_disposed)
        {
            Thread.Sleep(_options.WatchdogPeriodMs);
            if (_disposed)
            {
                break;
            }

            var now = DateTime.UtcNow;
            WorkerContext[] snapshot;
            lock (_workersLock)
            {
                snapshot = _workerContexts.ToArray();
            }

            foreach (var ctx in snapshot)
            {
                if (!ctx.Busy)
                {
                    continue;
                }

                var elapsed = (now - ctx.WorkStartUtc).TotalMilliseconds;
                if (elapsed <= _options.StuckThreadTimeoutMs)
                {
                    continue;
                }

                _options.LogInfo?.Invoke(
                    $"Зависший поток (>{_options.StuckThreadTimeoutMs} мс): добавление замены. Worker #{ctx.Id}");
                TryAddReplacementWorker();
                ctx.WorkStartUtc = now;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Raise(PoolDisposed);

        _disposed = true;

        lock (_workersLock)
        {
            var n = _workerContexts.Count + 64;
            for (var i = 0; i < n; i++)
            {
                _workSignal.Release();
            }
        }

        Thread[] threads;
        lock (_workersLock)
        {
            threads = _workerContexts.Select(c => c.WorkerThread).Where(t => t != null).Cast<Thread>().ToArray();
        }

        foreach (var t in threads)
        {
            t.Join(TimeSpan.FromSeconds(3));
        }

        _watchdogThread.Join(TimeSpan.FromSeconds(2));
        _workSignal.Dispose();
        _drained.Dispose();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnterQueueLock()
    {
        Monitor.Enter(_queueLock);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExitQueueLock()
    {
        Monitor.Exit(_queueLock);
    }

    private sealed class WorkItem(Action action, DateTime enqueuedUtc)
    {
        public Action Action { get; } = action;
        public DateTime EnqueuedUtc { get; } = enqueuedUtc;
    }

    private sealed class WorkerContext
    {
        private static int _nextId;

        public readonly int Id = Interlocked.Increment(ref _nextId);
        public volatile bool Busy;
        public DateTime WorkStartUtc;
        public Thread? WorkerThread;
    }

    private int GetQueueLengthLocked()
    {
        EnterQueueLock();
        try
        {
            return _queue.Count;
        }
        finally
        {
            ExitQueueLock();
        }
    }

    private void Raise(EventHandler? handler)
    {
        handler?.Invoke(this, EventArgs.Empty);
    }

    private void Raise<T>(EventHandler<T>? handler, T args) where T : EventArgs
    {
        handler?.Invoke(this, args);
    }
}
