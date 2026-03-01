using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using TestLibrary;

namespace TestRunner
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("Запуск кастомного средства тестирования...\n");

            // Пытаемся найти сборку TestProject среди уже загруженных в память 
            Assembly testAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "TestProject");

            if (testAssembly == null)
            {
                // Пытаемся загрузить явно
                try 
                {
                    testAssembly = Assembly.Load("TestProject");
                }
                catch
                {
                    Console.WriteLine("Сборка TestProject не найдена.");
                    return;
                }
            }

            int totalTests = 0;
            int passedTests = 0;
            int failedTests = 0;

            var testClasses = testAssembly.GetTypes()
                .Where(t => t.GetCustomAttribute<TestClassAttribute>() != null);

            foreach (var testClass in testClasses)
            {
                var classAttr = testClass.GetCustomAttribute<TestClassAttribute>();
                Console.WriteLine($"\n--- Класс тестов: {testClass.Name} " + (string.IsNullOrEmpty(classAttr?.Description) ? "" : $"[{classAttr.Description}]") + " ---");

                var methods = testClass.GetMethods();
                var testMethods = methods.Where(m => m.GetCustomAttribute<TestMethodAttribute>() != null).ToList();
                var setupMethod = methods.FirstOrDefault(m => m.GetCustomAttribute<SetupAttribute>() != null);
                var teardownMethod = methods.FirstOrDefault(m => m.GetCustomAttribute<TeardownAttribute>() != null);

                if (!testMethods.Any())
                {
                    Console.WriteLine("  Тесты не найдены.");
                    continue;
                }

                object? instance = Activator.CreateInstance(testClass);

                foreach (var testMethod in testMethods)
                {
                    totalTests++;
                    var methodAttr = testMethod.GetCustomAttribute<TestMethodAttribute>();
                    string testName = string.IsNullOrEmpty(methodAttr?.Description) ? testMethod.Name : methodAttr.Description;
                    
                    Console.Write($"  Выполнение: {testName} ... ");

                    try
                    {
                        // Setup
                        setupMethod?.Invoke(instance, null);

                        // Запуск теста
                        if (testMethod.ReturnType == typeof(Task))
                        {
                            await (Task)(testMethod.Invoke(instance, null) ?? Task.CompletedTask);
                        }
                        else
                        {
                            testMethod.Invoke(instance, null);
                        }

                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("УСПЕШНО");
                        Console.ResetColor();
                        passedTests++;
                    }
                    catch (TargetInvocationException ex)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("ПРОВАЛЕНО");
                        Console.ResetColor();
                        failedTests++;

                        Exception? actualEx = ex.InnerException;
                        if (actualEx is AssertFailedException)
                        {
                            Console.WriteLine($"    Ошибка проверки: {actualEx.Message}");
                        }
                        else if (actualEx != null)
                        {
                            Console.WriteLine($"    Непредвиденное исключение: {actualEx.GetType().Name} - {actualEx.Message}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("ПРОВАЛЕНО (Неизвестная ошибка)");
                        Console.ResetColor();
                        failedTests++;
                        Console.WriteLine($"    {ex.Message}");
                    }
                    finally
                    {
                        // Teardown
                        try
                        {
                            teardownMethod?.Invoke(instance, null);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"    Ошибка в Teardown: {ex.InnerException?.Message ?? ex.Message}");
                        }
                    }
                }
            }

            Console.WriteLine("\n========================================");
            Console.WriteLine($"Всего тестов: {totalTests}");
            Console.WriteLine($"Успешно: {passedTests}");
            Console.WriteLine($"Провалено: {failedTests}");
            Console.WriteLine("========================================");
        }
    }
}
