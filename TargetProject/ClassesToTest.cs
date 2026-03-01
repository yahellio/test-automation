using System;
using System.Threading.Tasks;

namespace TargetProject
{
    public class Calculator
    {
        public int Add(int a, int b) => a + b;
        public int Divide(int a, int b) => a / b;
    }

    public class StringManipulator
    {
        public string Reverse(string input)
        {
            if (input == null) return null;
            char[] array = input.ToCharArray();
            Array.Reverse(array);
            return new string(array);
        }

        public string Concatenate(string s1, string s2) => s1 + s2;
    }

    public class AsyncService
    {
        public async Task<int> FetchDataAsync()
        {
            await Task.Delay(100); // Simulate work
            return 42;
        }

        public async Task FailAsync()
        {
            await Task.Delay(50);
            throw new InvalidOperationException("Failed intentionally.");
        }
    }

}
