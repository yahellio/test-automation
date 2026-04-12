using System.Collections;
using System.Reflection;
using TestLibrary;

namespace TestRunner;

internal sealed record TestCase(
    Type TestClass,
    MethodInfo TestMethod,
    MethodInfo? SetupMethod,
    MethodInfo? TeardownMethod,
    TestMethodAttribute? MethodAttribute,
    string DisplayName,
    object[]? RowParameters,
    IReadOnlyList<string> Categories,
    int? Priority,
    string? Author);

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

                var categories = CollectCategories(testClass, testMethod);
                var priority = testMethod.GetCustomAttribute<PriorityAttribute>()?.Level;
                var author = testMethod.GetCustomAttribute<AuthorAttribute>()?.Name;

                var sourceAttr = testMethod.GetCustomAttribute<TestCaseSourceAttribute>();
                if (sourceAttr != null)
                {
                    var rows = ExpandFromSource(testClass, testMethod, sourceAttr.MethodName);
                    for (var i = 0; i < rows.Count; i++)
                    {
                        var row = rows[i];
                        var rowLabel = string.Join(", ", row.Select(FormatCell));
                        result.Add(new TestCase(
                            testClass,
                            testMethod,
                            setupMethod,
                            teardownMethod,
                            methodAttribute,
                            $"{testClass.Name}: {methodName} [набор {i + 1}: {rowLabel}]",
                            row,
                            categories,
                            priority,
                            author));
                    }
                }
                else
                {
                    result.Add(new TestCase(
                        testClass,
                        testMethod,
                        setupMethod,
                        teardownMethod,
                        methodAttribute,
                        $"{testClass.Name}: {methodName}",
                        null,
                        categories,
                        priority,
                        author));
                }
            }
        }

        return result;
    }

    internal static List<TestCase> FilterTests(IEnumerable<TestCase> tests, Func<TestCase, bool> predicate)
    {
        return tests.Where(predicate).ToList();
    }

    internal static async Task<TestExecutionResult> ExecuteTestAsync(TestCase testCase)
    {
        object? instance;
        try
        {
            instance = testCase.TestMethod.IsStatic ? null : Activator.CreateInstance(testCase.TestClass);
        }
        catch (Exception ex)
        {
            return new TestExecutionResult(false, $"Не удалось создать класс теста: {ex.Message}");
        }

        object[]? parameters;
        try
        {
            parameters = BuildParameters(testCase);
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

    private static object[]? BuildParameters(TestCase testCase)
    {
        if (testCase.RowParameters != null)
        {
            return testCase.RowParameters;
        }

        var testMethod = testCase.TestMethod;
        var methodParameters = testMethod.GetParameters();

        if (methodParameters.Length == 0)
        {
            return null;
        }

        if (methodParameters.Length == 1 && methodParameters[0].ParameterType == typeof(int))
        {
            return new object[] { testCase.MethodAttribute?.Data ?? 0 };
        }

        throw new InvalidOperationException(
            $"Тестовый метод {testMethod.Name} имеет неподдерживаемую сигнатуру. " +
            "Укажите [TestCaseSource] или один параметр int через [TestMethod(..., data: n)].");
    }

    private static IReadOnlyList<string> CollectCategories(Type testClass, MethodInfo method)
    {
        var list = new List<string>();
        foreach (var a in testClass.GetCustomAttributes<CategoryAttribute>())
        {
            list.Add(a.Name);
        }

        foreach (var a in method.GetCustomAttributes<CategoryAttribute>())
        {
            list.Add(a.Name);
        }

        return list;
    }

    private static List<object[]> ExpandFromSource(Type testClass, MethodInfo testMethod, string sourceMethodName)
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        var source = testClass.GetMethod(sourceMethodName, flags);
        if (source == null)
        {
            throw new InvalidOperationException(
                $"Не найден статический метод «{sourceMethodName}» в классе {testClass.Name}.");
        }

        if (source.GetParameters().Length != 0)
        {
            throw new InvalidOperationException($"Метод источника «{sourceMethodName}» должен быть без параметров.");
        }

        var raw = source.Invoke(null, null);
        if (raw is not IEnumerable seq)
        {
            throw new InvalidOperationException($"Метод «{sourceMethodName}» должен вернуть IEnumerable.");
        }

        var paramInfos = testMethod.GetParameters();
        var list = new List<object[]>();

        foreach (var item in seq)
        {
            if (item is not object[] row)
            {
                throw new InvalidOperationException(
                    $"Каждый элемент из «{sourceMethodName}» должен быть object[].");
            }

            if (row.Length != paramInfos.Length)
            {
                throw new InvalidOperationException(
                    $"Для {testMethod.Name} ожидалось {paramInfos.Length} значений в строке, получено {row.Length}.");
            }

            list.Add(row);
        }

        return list;
    }

    private static string FormatCell(object? value)
    {
        return value switch
        {
            null => "null",
            IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture) ?? "",
            _ => value.ToString() ?? ""
        };
    }
}
