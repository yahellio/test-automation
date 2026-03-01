using System;
using System.Threading.Tasks;
using TestLibrary;
using TargetProject;

namespace TestProject
{
    [TestClass("Tests for Calculator class")]
    public class CalculatorTests
    {
        private Calculator _calculator;

        [Setup]
        public void Init()
        {
            _calculator = new Calculator();
        }

        [Teardown]
        public void CleanUp()
        {
            _calculator = null;
        }

        [TestMethod("Adding two positive numbers", timeout: 100)]
        public void Add_PositiveNumbers_ReturnsSum()
        {
            int result = _calculator.Add(2, 3);
            Assert.AreEqual(5, result);
        }

        [TestMethod("Division by zero throws exception")]
        public void Divide_ByZero_ThrowsException()
        {
            Assert.Throws<DivideByZeroException>(() => _calculator.Divide(10, 0));
        }
    }

    [TestClass]
    public class StringManipulatorTests
    {
        private StringManipulator _manipulator = new StringManipulator();

        [TestMethod]
        public void Reverse_ValidString_ReversesString()
        {
            string result = _manipulator.Reverse("hello");
            Assert.AreNotEqual("hello", result);
            Assert.AreEqual("olleh", result);
        }

        [TestMethod]
        public void Concatenate_Strings_ContainsSubstring()
        {
            string result = _manipulator.Concatenate("foo", "bar");
            Assert.Contains("ooba", result);
            Assert.IsTrue(result.Length == 6);
            Assert.IsFalse(result.Length == 5);
        }

        [TestMethod]
        public void Reverse_Null_ReturnsNull()
        {
            string result = _manipulator.Reverse(null);
            Assert.IsNull(result);
        }
        
        [TestMethod]
        public void Reverse_Empty_ReturnsNotNull()
        {
            string result = _manipulator.Reverse("");
            Assert.IsNotNull(result);
        }
    }

    [TestClass("Async tests")]
    public class AsyncServiceTests
    {
        private AsyncService _service = new AsyncService();

        [TestMethod("Test fetching data asynchronously")]
        public async Task FetchDataAsync_ReturnsValue()
        {
            int result = await _service.FetchDataAsync();
            Assert.AreEqual(42, result);
        }

        [TestMethod]
        public async Task FailAsync_ThrowsInvalidOperationException()
        {
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await _service.FailAsync());
        }
    }

    [TestClass("Reference tests")]
    public class ReferenceTests
    {
        [TestMethod]
        public void Objects_AreSame_AreNotSame()
        {
            object obj1 = new object();
            object obj2 = obj1;
            object obj3 = new object();

            Assert.AreSame(obj1, obj2);
            Assert.AreNotSame(obj1, obj3);
        }
        
        [TestMethod]
        public void FailingTest_ForDemonstration()
        {
            // This test is supposed to fail to demonstrate error handling
            Assert.AreEqual(1, 2);
        }
    }
}
