using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;

namespace TestLibrary
{
    public class AssertFailedException : Exception
    {
        public AssertFailedException(string message) : base(message)
        {
        }
    }

    public static class Assert
    {
        public static void AreEqual<T>(T expected, T actual)
        {
            if (!object.Equals(expected, actual))
            {
                throw new AssertFailedException($"Expected: <{expected}>, Actual: <{actual}>");
            }
        }

        public static void AreNotEqual<T>(T expected, T actual)
        {
            if (object.Equals(expected, actual))
            {
                throw new AssertFailedException($"Expected any value except: <{expected}>, Actual: <{actual}>");
            }
        }

        public static void IsTrue(bool condition)
        {
            if (!condition)
            {
                throw new AssertFailedException("Expected: <True>, Actual: <False>");
            }
        }

        // Проверка по дереву выражений
        public static void IsTrue(Expression<Func<bool>> condition)
        {
            if (condition.Compile()())
                return;

            var p = condition.Parameters;
            var body = condition.Body;

            string opLine;
            string structure = FormatNodeStructure(body);
            string valuesLine;

            switch (body)
            {
                case BinaryExpression b:
                    opLine = BinaryOpName(b.NodeType);
                    valuesLine = $"левый: {Eval(b.Left, p)}, правый: {Eval(b.Right, p)}";
                    break;
                case UnaryExpression u:
                    opLine = u.NodeType.ToString();
                    valuesLine = $"операнд: {Eval(u.Operand, p)}, результат: {Eval(u, p)}";
                    break;
                default:
                    opLine = body.NodeType.ToString();
                    valuesLine = Eval(body, p);
                    break;
            }

            throw new AssertFailedException(
                "Оператор: " + opLine + "\n" +
                "Структура выражения: " + structure + "\n" +
                "Значения операндов: " + valuesLine);
        }

        // Краткое текстовое дерево по типам узлов
        private static string FormatNodeStructure(Expression e, int depth = 0, int maxDepth = 4)
        {
            if (depth >= maxDepth)
                return e.NodeType.ToString();

            return e switch
            {
                BinaryExpression b =>
                    $"{b.NodeType}({FormatNodeStructure(b.Left, depth + 1, maxDepth)}, {FormatNodeStructure(b.Right, depth + 1, maxDepth)})",
                UnaryExpression u =>
                    $"{u.NodeType}({FormatNodeStructure(u.Operand, depth + 1, maxDepth)})",
                _ => e.NodeType.ToString()
            };
        }

        private static string BinaryOpName(ExpressionType t) =>
            t switch
            {
                ExpressionType.Equal => "==",
                ExpressionType.NotEqual => "!=",
                ExpressionType.GreaterThan => ">",
                ExpressionType.LessThan => "<",
                ExpressionType.GreaterThanOrEqual => ">=",
                ExpressionType.LessThanOrEqual => "<=",
                ExpressionType.AndAlso => "&&",
                ExpressionType.OrElse => "||",
                ExpressionType.Add or ExpressionType.AddChecked => "+",
                ExpressionType.Subtract or ExpressionType.SubtractChecked => "-",
                ExpressionType.Multiply or ExpressionType.MultiplyChecked => "*",
                ExpressionType.Divide => "/",
                _ => t.ToString()
            };

        //вычисляем значение произвольного выражения
        private static string Eval(Expression node, IReadOnlyList<ParameterExpression> parameters)
        {
            try
            {
                var lambda = Expression.Lambda(node, parameters);
                var d = lambda.Compile();
                var v = d.DynamicInvoke();
                return v is null ? "null" : v.ToString() ?? "";
            }
            catch
            {
                return "(невычислимо)";
            }
        }

        public static void IsFalse(bool condition)
        {
            if (condition)
            {
                throw new AssertFailedException("Expected: <False>, Actual: <True>");
            }
        }

        public static void IsNull(object obj)
        {
            if (obj != null)
            {
                throw new AssertFailedException("Expected: <null>, Actual: not null");
            }
        }

        public static void IsNotNull(object obj)
        {
            if (obj == null)
            {
                throw new AssertFailedException("Expected: not null, Actual: <null>");
            }
        }

        public static void AreSame(object expected, object actual)
        {
            if (!ReferenceEquals(expected, actual))
            {
                throw new AssertFailedException("Expected same references, but they are different.");
            }
        }

        public static void AreNotSame(object expected, object actual)
        {
            if (ReferenceEquals(expected, actual))
            {
                throw new AssertFailedException("Expected different references, but they are the same.");
            }
        }

        public static void Contains(string expectedSubstring, string actualString)
        {
            if (actualString == null || !actualString.Contains(expectedSubstring))
            {
                throw new AssertFailedException($"String <{actualString}> does not contain <{expectedSubstring}>");
            }
        }

        public static void Throws<T>(Action action) where T : Exception
        {
            try
            {
                action();
            }
            catch (T)
            {
                return;
            }
            catch (Exception ex)
            {
                throw new AssertFailedException($"Expected exception of type {typeof(T).Name}, but {ex.GetType().Name} was thrown.");
            }
            throw new AssertFailedException($"Expected exception of type {typeof(T).Name}, but no exception was thrown.");
        }

        public static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception
        {
            try
            {
                await action();
            }
            catch (T)
            {
                return;
            }
            catch (Exception ex)
            {
                throw new AssertFailedException($"Expected exception of type {typeof(T).Name}, but {ex.GetType().Name} was thrown.");
            }
            throw new AssertFailedException($"Expected exception of type {typeof(T).Name}, but no exception was thrown.");
        }
    }
}
