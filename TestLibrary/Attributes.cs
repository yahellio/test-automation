using System;

namespace TestLibrary
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class TestCaseSourceAttribute : Attribute
    {
        public string MethodName { get; }

        public TestCaseSourceAttribute(string methodName)
        {
            MethodName = methodName;
        }
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public sealed class CategoryAttribute : Attribute
    {
        public string Name { get; }

        public CategoryAttribute(string name)
        {
            Name = name;
        }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class PriorityAttribute : Attribute
    {
        public int Level { get; }

        public PriorityAttribute(int level)
        {
            Level = level;
        }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class AuthorAttribute : Attribute
    {
        public string Name { get; }

        public AuthorAttribute(string name)
        {
            Name = name;
        }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public class TestClassAttribute : Attribute
    {
        public string Description { get; set; }

        public TestClassAttribute(string description = "")
        {
            Description = description;
        }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class TestMethodAttribute : Attribute
    {
        public string Description { get; set; }
        public int Timeout { get; set; }
        public int Data { get; set; }

        public TestMethodAttribute(string description = "", int timeout = 0, int data = 0)
        {
            Description = description;
            Timeout = timeout;
            Data = data;
        }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class SetupAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class TimeoutAttribute : Attribute
    {
        public int Milliseconds { get; }

        public TimeoutAttribute(int milliseconds)
        {
            Milliseconds = milliseconds;
        }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class TeardownAttribute : Attribute
    {
    }
}
