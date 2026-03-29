using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using TestLibrary;

namespace TestRunner
{
    internal static class Program
    {
        private const int DefaultMaxDegreeOfParallelism = 4;

        private sealed record TestCase(
            Type TestClass,
            MethodInfo TestMethod,
            MethodInfo? SetupMethod,
            MethodInfo? TeardownMethod,
            TestMethodAttribute? MethodAttribute,
            string DisplayName);

        private sealed record TestRunResult(int Total, int Passed, int Failed, TimeSpan Elapsed);

        private sealed record TestExecutionResult(bool IsSuccess, string ErrorMessage);

        private static async Task Main(string[] args)
        {
            Console.WriteLine("Запуск средства тестирования...\n");

            var testAssembly = LoadTestAssembly();
            if (testAssembly == null)
            {
                Console.WriteLine("Сборка TestProject не найдена.");
                return;
            }

            var testCases = DiscoverTestCases(testAssembly);
            if (testCases.Count == 0)
            {
                Console.WriteLine("Тесты не найдены.");
                return;
            }

            var maxDegreeOfParallelism = ParseMaxDegreeOfParallelism(args);

            Console.WriteLine($"Найдено тестов: {testCases.Count}");
            Console.WriteLine($"Лимит параллельных потоков: {maxDegreeOfParallelism}");

            var sequential = await RunAllTestsAsync(testCases, 1, "Последовательный запуск");
            var parallel = await RunAllTestsAsync(
                testCases,
                maxDegreeOfParallelism,
                "Параллельный запуск");

            PrintComparison(sequential, parallel);
        }

        private static int ParseMaxDegreeOfParallelism(string[] args)
        {
            if (args.Length == 0)
            {
                return DefaultMaxDegreeOfParallelism;
            }

            if (int.TryParse(args[0], out var parsedValue) && parsedValue > 0)
            {
                return parsedValue;
            }

            Console.WriteLine(
                $"Некорректный MaxDegreeOfParallelism \"{args[0]}\". " +
                $"Используется значение по умолчанию: {DefaultMaxDegreeOfParallelism}.");
            return DefaultMaxDegreeOfParallelism;
        }

        private static Assembly? LoadTestAssembly()
        {
            var alreadyLoaded = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "TestProject");

            if (alreadyLoaded != null)
            {
                return alreadyLoaded;
            }

            try
            {
                return Assembly.Load("TestProject");
            }
            catch
            {
                return null;
            }
        }

        private static List<TestCase> DiscoverTestCases(Assembly testAssembly)
        {
            var result = new List<TestCase>();

            var testClasses = testAssembly.GetTypes()
                .Where(t => t.GetCustomAttribute<TestClassAttribute>() != null);

            foreach (var testClass in testClasses)
            {
                var methods = testClass.GetMethods();
                var testMethods = methods
                    .Where(m => m.GetCustomAttribute<TestMethodAttribute>() != null)
                    .ToList();
                var setupMethod = methods.FirstOrDefault(m => m.GetCustomAttribute<SetupAttribute>() != null);
                var teardownMethod = methods.FirstOrDefault(m => m.GetCustomAttribute<TeardownAttribute>() != null);

                foreach (var testMethod in testMethods)
                {
                    var methodAttribute = testMethod.GetCustomAttribute<TestMethodAttribute>();
                    var methodName = string.IsNullOrEmpty(methodAttribute?.Description)
                        ? testMethod.Name
                        : methodAttribute.Description;

                    result.Add(new TestCase(
                        testClass,
                        testMethod,
                        setupMethod,
                        teardownMethod,
                        methodAttribute,
                        $"{testClass.Name}: {methodName}"));
                }
            }

            return result;
        }

        private static async Task<TestRunResult> RunAllTestsAsync(
            IReadOnlyCollection<TestCase> testCases,
            int maxDegreeOfParallelism,
            string runTitle)
        {
            var consoleLock = new object();
            int passedTests = 0;
            int failedTests = 0;

            var stopwatch = Stopwatch.StartNew();

            Console.WriteLine($"\n=== {runTitle} ===");

            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = maxDegreeOfParallelism
            };

            await Parallel.ForEachAsync(testCases, options, async (testCase, _) =>
            {
                var executionResult = await ExecuteTestAsync(testCase);

                lock (consoleLock)
                {
                    Console.Write($"  {testCase.DisplayName} ... ");
                    if (executionResult.IsSuccess)
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("УСПЕШНО");
                        Console.ResetColor();
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("ПРОВАЛЕНО");
                        Console.ResetColor();
                        Console.WriteLine($"    {executionResult.ErrorMessage}");
                    }
                }

                if (executionResult.IsSuccess)
                {
                    Interlocked.Increment(ref passedTests);
                }
                else
                {
                    Interlocked.Increment(ref failedTests);
                }
            });

            stopwatch.Stop();

            var result = new TestRunResult(
                testCases.Count,
                passedTests,
                failedTests,
                stopwatch.Elapsed);

            Console.WriteLine("----------------------------------------");
            Console.WriteLine($"Всего тестов: {result.Total}");
            Console.WriteLine($"Успешно: {result.Passed}");
            Console.WriteLine($"Провалено: {result.Failed}");
            Console.WriteLine($"Время: {result.Elapsed.TotalMilliseconds:F0} мс");
            Console.WriteLine("----------------------------------------");

            return result;
        }

        private static async Task<TestExecutionResult> ExecuteTestAsync(TestCase testCase)
        {
            object? instance;
            try
            {
                instance = Activator.CreateInstance(testCase.TestClass);
            }
            catch (Exception ex)
            {
                return new TestExecutionResult(false, $"Не удалось создать класс теста: {ex.Message}");
            }

            object[]? parameters;
            try
            {
                parameters = BuildParameters(testCase.TestMethod, testCase.MethodAttribute);
            }
            catch (Exception ex)
            {
                return new TestExecutionResult(false, ex.Message);
            }

            var timeoutMs = ResolveTimeout(testCase.TestMethod, testCase.MethodAttribute);

            try
            {
                var executionTask = ExecuteTestBodyAsync(testCase, instance, parameters);

                if (timeoutMs > 0)
                {
                    var completedTask = await Task.WhenAny(executionTask, Task.Delay(timeoutMs));
                    if (completedTask != executionTask)
                    {
                        return new TestExecutionResult(false, $"Превышено время ожидания ({timeoutMs} мс)");
                    }
                }

                await executionTask;
                return new TestExecutionResult(true, string.Empty);
            }
            catch (Exception ex)
            {
                var actualEx = UnwrapException(ex);
                if (actualEx is AssertFailedException)
                {
                    return new TestExecutionResult(false, $"Ошибка проверки: {actualEx.Message}");
                }

                return new TestExecutionResult(false, $"{actualEx.GetType().Name}: {actualEx.Message}");
            }
        }

        private static async Task ExecuteTestBodyAsync(TestCase testCase, object? instance, object[]? parameters)
        {
            try
            {
                testCase.SetupMethod?.Invoke(instance, null);

                var invokeResult = testCase.TestMethod.Invoke(instance, parameters);
                if (invokeResult is Task task)
                {
                    await task;
                }
            }
            finally
            {
                testCase.TeardownMethod?.Invoke(instance, null);
            }
        }

        private static int ResolveTimeout(MethodInfo testMethod, TestMethodAttribute? methodAttr)
        {
            var timeoutAttribute = testMethod.GetCustomAttribute<TimeoutAttribute>();
            if (timeoutAttribute != null && timeoutAttribute.Milliseconds > 0)
            {
                return timeoutAttribute.Milliseconds;
            }

            if (methodAttr != null && methodAttr.Timeout > 0)
            {
                return methodAttr.Timeout;
            }

            return 0;
        }

        private static Exception UnwrapException(Exception ex)
        {
            if (ex is TargetInvocationException targetInvocationException && targetInvocationException.InnerException != null)
            {
                return UnwrapException(targetInvocationException.InnerException);
            }

            if (ex is AggregateException aggregateException && aggregateException.InnerException != null)
            {
                return UnwrapException(aggregateException.InnerException);
            }

            return ex;
        }

        private static object[]? BuildParameters(MethodInfo testMethod, TestMethodAttribute? methodAttr)
        {
            var methodParameters = testMethod.GetParameters();

            if (methodParameters.Length == 0)
            {
                return null;
            }

            if (methodParameters.Length == 1 && methodParameters[0].ParameterType == typeof(int))
            {
                return new object[] { methodAttr?.Data ?? 0 };
            }

            throw new InvalidOperationException(
                $"Тестовый метод {testMethod.Name} имеет неподдерживаемую сигнатуру. " +
                "Разрешены методы без параметров или с одним параметром типа int.");
        }

        private static void PrintComparison(TestRunResult sequential, TestRunResult parallel)
        {
            Console.WriteLine("\n========================================");
            Console.WriteLine("Сравнение производительности");
            Console.WriteLine($"Последовательно: {sequential.Elapsed.TotalMilliseconds:F0} мс");
            Console.WriteLine($"Параллельно:    {parallel.Elapsed.TotalMilliseconds:F0} мс");

            var diff = sequential.Elapsed.TotalMilliseconds - parallel.Elapsed.TotalMilliseconds;
            if (diff > 0)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"Параллельный запуск быстрее на {diff:F0} мс");
            }
            else if (diff < 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"Последовательный запуск быстрее на {Math.Abs(diff):F0} мс");
            }
            else
            {
                Console.WriteLine("Время выполнения одинаковое");
            }

            Console.ResetColor();
            Console.WriteLine("========================================");
        }
    }
}
