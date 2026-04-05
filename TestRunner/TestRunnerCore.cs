using System.Reflection;
using TestLibrary;

namespace TestRunner;

internal sealed record TestCase(
    Type TestClass,
    MethodInfo TestMethod,
    MethodInfo? SetupMethod,
    MethodInfo? TeardownMethod,
    TestMethodAttribute? MethodAttribute,
    string DisplayName);

internal sealed record TestExecutionResult(bool IsSuccess, string ErrorMessage);

internal static class TestRunnerCore
{
    internal static Assembly? LoadTestAssembly()
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

    internal static List<TestCase> DiscoverTestCases(Assembly testAssembly)
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

    internal static async Task<TestExecutionResult> ExecuteTestAsync(TestCase testCase)
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
}
