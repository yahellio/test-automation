using System;

namespace TestLibrary
{
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
