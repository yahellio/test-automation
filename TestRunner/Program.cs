using System.Diagnostics;
using System.Threading;
using ThreadPoolModule;

namespace TestRunner;

internal static class Program
{
    private const int MinTotalTestRuns = 50;

    private static readonly object ConsoleLock = new();

    private static void Main()
    {
        Console.WriteLine("Запуск средства тестирования \n");

        var testAssembly = TestRunnerCore.LoadTestAssembly();
        if (testAssembly == null)
        {
            Console.WriteLine("Сборка TestProject не найдена.");
            return;
        }

        var testCases = TestRunnerCore.DiscoverTestCases(testAssembly);
        if (testCases.Count == 0)
        {
            Console.WriteLine("Тесты не найдены.");
            return;
        }

        var catalog = testCases.ToList();
        var passes = (int)Math.Ceiling(MinTotalTestRuns / (double)catalog.Count);
        var plannedRuns = passes * catalog.Count;

        Console.WriteLine($"Тестов в каталоге: {catalog.Count}");
        Console.WriteLine($"Полных прогонов: {passes} (выполнений тестов: {plannedRuns}, требование ≥{MinTotalTestRuns})\n");

        var baseLogError = new Action<Exception>(ex =>
        {
            lock (ConsoleLock)
            {
                Console.WriteLine($"[Ошибка пула] {ex.GetType().Name}: {ex.Message}");
            }
        });

        var single = RunPhase(
            new DynamicThreadPoolOptions
            {
                MinThreads = 1,
                MaxThreads = 1,
                QueueLengthScaleThreshold = 100,
                OldestTaskWaitThresholdMs = 60_000,
                IdleTimeoutMs = 60_000,
                StuckThreadTimeoutMs = 120_000,
                WatchdogPeriodMs = 1000,
                LogError = baseLogError
            },
            catalog,
            passes,
            "Фаза 1: один поток (как последовательное выполнение)",
            monitor: false);

        var dynamic = RunPhase(
            new DynamicThreadPoolOptions
            {
                MinThreads = 2,
                MaxThreads = 8,
                QueueLengthScaleThreshold = 2,
                OldestTaskWaitThresholdMs = 150,
                IdleTimeoutMs = 1200,
                StuckThreadTimeoutMs = 12000,
                WatchdogPeriodMs = 350,
                LogInfo = msg =>
                {
                    lock (ConsoleLock)
                    {
                        Console.WriteLine($"[пул] {msg}");
                    }
                },
                LogError = baseLogError
            },
            catalog,
            passes,
            "Фаза 2: динамический пул",
            monitor: true);

        Console.WriteLine("\n========================================");
        Console.WriteLine("Сравнение времени (стена)");
        Console.WriteLine($"Один поток:        {single.ElapsedMs:F0} мс");
        Console.WriteLine($"Динамический пул:  {dynamic.ElapsedMs:F0} мс");
        var diff = single.ElapsedMs - dynamic.ElapsedMs;
        if (diff > 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Динамический пул быстрее на {diff:F0} мс");
        }
        else if (diff < 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"Один поток быстрее на {Math.Abs(diff):F0} мс");
        }
        else
        {
            Console.WriteLine("Время одинаковое");
        }

        Console.ResetColor();
        Console.WriteLine($"Макс. потоков (фаза 2): {dynamic.MaxThreadsObserved} (лимит {dynamic.PoolMaxThreads})");
        Console.WriteLine("========================================");
    }

    private sealed record PhaseResult(double ElapsedMs, int MaxThreadsObserved, int PoolMaxThreads);

    private static PhaseResult RunPhase(
        DynamicThreadPoolOptions poolOptions,
        List<TestCase> catalog,
        int passes,
        string title,
        bool monitor)
    {
        lock (ConsoleLock)
        {
            Console.WriteLine($"\n=== {title} ===\n");
        }

        using var pool = new DynamicThreadPool(poolOptions);

        long passed = 0;
        long failed = 0;
        long completed = 0;

        using var monitorCts = new CancellationTokenSource();
        MonitorStats? stats = null;
        Thread? monitorThread = null;
        if (monitor)
        {
            stats = new MonitorStats();
            monitorThread = new Thread(() => MonitorLoop(pool, monitorCts.Token, stats))
            {
                IsBackground = true,
                Name = "Pool-Monitor"
            };
            monitorThread.Start();
        }

        var wall = Stopwatch.StartNew();

        EnqueueScenario(catalog, passes, (tc, pass) =>
        {
            //подача задач в пул
            pool.Enqueue(() =>
            {
                var r = TestRunnerCore.ExecuteTestAsync(tc).GetAwaiter().GetResult();
                Interlocked.Increment(ref completed);
                if (r.IsSuccess)
                {
                    Interlocked.Increment(ref passed);
                }
                else
                {
                    Interlocked.Increment(ref failed);
                }

                lock (ConsoleLock)
                {
                    Console.Write($"  {tc.DisplayName} [прогон {pass}] ... ");
                    if (r.IsSuccess)
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("УСПЕШНО");
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("ПРОВАЛЕНО");
                        Console.WriteLine($"    {r.ErrorMessage}");
                    }

                    Console.ResetColor();
                }
            });
        });

        //блокирует текущий поток, пока все задачи не будут выполнены
        pool.WaitForDrain();
        wall.Stop();
        monitorCts.Cancel();
        monitorThread?.Join(TimeSpan.FromSeconds(2));

        var snap = pool.GetSnapshot();
        var maxObserved = stats?.MaxActiveThreads ?? poolOptions.MaxThreads;

        lock (ConsoleLock)
        {
            Console.WriteLine("----------------------------------------");
            Console.WriteLine(
                $"Завершено: {completed}, успешно: {passed}, провалено: {failed}, время: {wall.Elapsed.TotalMilliseconds:F0} мс");
            if (monitor)
            {
                Console.WriteLine(
                    $"Замены при зависании: {snap.StuckReplacements}, сбоев воркеров: {snap.WorkerFaultRecoveries}");
            }
        }

        return new PhaseResult(wall.Elapsed.TotalMilliseconds, maxObserved, poolOptions.MaxThreads);
    }

    private sealed class MonitorStats
    {
        public int MaxActiveThreads;
        public readonly object Gate = new();
    }

    private static void EnqueueScenario(
        IReadOnlyList<TestCase> catalog,
        int passes,
        Action<TestCase, int> enqueueOne)
    {
        lock (ConsoleLock)
        {
            Console.WriteLine("Сценарий: пауза → пик → пауза → единичные → пауза → полные прогоны\n");
        }

        Thread.Sleep(400);

        lock (ConsoleLock)
        {
            Console.WriteLine("--- Пик (пачка) ---");
        }
        for (var i = 0; i < Math.Min(8, catalog.Count); i++)
        {
            enqueueOne(catalog[i], 1);
        }

        Thread.Sleep(350);

        lock (ConsoleLock)
        {
            Console.WriteLine("--- Единичные подачи ---");
        }
        for (var j = 0; j < 5 && j < catalog.Count; j++)
        {
            enqueueOne(catalog[j], 1);
            Thread.Sleep(120);
        }

        Thread.Sleep(500);

        lock (ConsoleLock)
        {
            Console.WriteLine("--- Пауза (имитация простоя; во 2-й фазе пул может сократить число потоков) ---");
        }

        Thread.Sleep(900);

        lock (ConsoleLock)
        {
            Console.WriteLine($"--- {passes} полных прогонов ---");
        }
        for (var p = 1; p <= passes; p++)
        {
            foreach (var tc in catalog)
            {
                enqueueOne(tc, p);
            }

            if (p < passes)
            {
                Thread.Sleep(80);
            }
        }
    }

    private static void MonitorLoop(DynamicThreadPool pool, CancellationToken ct, MonitorStats stats)
    {
        while (!ct.IsCancellationRequested)
        {
            if (ct.WaitHandle.WaitOne(280))
            {
                break;
            }

            var s = pool.GetSnapshot();
            lock (stats.Gate)
            {
                if (s.ActiveThreads > stats.MaxActiveThreads)
                {
                    stats.MaxActiveThreads = s.ActiveThreads;
                }
            }

            lock (ConsoleLock)
            {
                Console.WriteLine(
                    $"[Состояние пула] активных потоков: {s.ActiveThreads}, " +
                    $"Задач в очереди: {s.QueueLength}, " +
                    $"Самая ранняя задача в очереди ждёт: {s.OldestWaitMs} мс, " +
                    $"Задач уже выполнено: {s.CompletedTasks}");
            }
        }
    }
}
