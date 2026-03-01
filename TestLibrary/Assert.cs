using System;
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
