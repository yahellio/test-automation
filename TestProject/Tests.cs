using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TestLibrary;
using TargetProject;

namespace TestProject
{
    [TestClass("Bank account tests")]
    [Category("Account")]
    public class BankAccountTests
    {
        private BankAccount _account;

        [Setup]
        public void Init()
        {
            _account = new BankAccount("ACC001", 1000m);
        }

        [Teardown]
        public void CleanUp()
        {
            _account = null;
        }

        public static IEnumerable<object[]> DepositAmountsSource()
        {
            yield return new object[] { 10m };
            yield return new object[] { 100m };
            yield return new object[] { 250m };
        }

        [TestMethod("Deposit from iterator source (parameterized)")]
        [TestCaseSource(nameof(DepositAmountsSource))]
        [Category("Parameterized")]
        [Priority(2)]
        [Author("LR4")]
        public void Deposit_FromSource_IncreasesBalance(decimal amount)
        {
            decimal initialBalance = _account.Balance;
            _account.Deposit(amount);
            Assert.IsTrue(() => _account.Balance == initialBalance + amount + 1m);
            //Assert.AreEqual(initialBalance + amount, _account.Balance);
        }

        [TestMethod("Deposit increases balance", timeout: 100, data: 500)]
        [Category("Smoke")]
        [Priority(1)]
        public void Deposit_ValidAmount_IncreasesBalance(int amount)
        {
            decimal initialBalance = _account.Balance;
            _account.Deposit(amount);
            Assert.AreEqual(initialBalance + amount, _account.Balance);
        }

        [TestMethod("Withdraw decreases balance")]
        [Category("Smoke")]
        [Priority(1)]
        public void Withdraw_ValidAmount_DecreasesBalance()
        {
            decimal initialBalance = _account.Balance;
            _account.Withdraw(300m);
            Assert.AreEqual(initialBalance - 300m, _account.Balance);
        }

        [TestMethod("Withdraw with insufficient funds throws exception")]
        public void Withdraw_InsufficientFunds_ThrowsException()
        {
            Assert.Throws<InvalidOperationException>(() => _account.Withdraw(2000m));
        }

        [TestMethod("Deposit negative amount throws exception")]
        public void Deposit_NegativeAmount_ThrowsException()
        {
            Assert.Throws<ArgumentException>(() => _account.Deposit(-100m));
        }

        [TestMethod("Locked account prevents operations")]
        public void LockedAccount_PreventsDeposit()
        {
            _account.Lock();
            Assert.IsTrue(_account.IsLocked);
            Assert.Throws<InvalidOperationException>(() => _account.Deposit(100m));
        }

        [TestMethod("Transaction history tracking")]
        public void Deposit_CreatesTransaction()
        {
            bool hadHistory = _account.HasTransactionHistory();
            _account.Deposit(100m);
            Assert.IsTrue(_account.HasTransactionHistory());
            Assert.IsNotNull(_account.GetLastTransaction());
        }

        [TestMethod("Account initialization with empty number throws exception")]
        public void Constructor_EmptyAccountNumber_ThrowsException()
        {
            Assert.Throws<ArgumentException>(() => new BankAccount(""));
        }

        [TestMethod("Account number contains expected prefix")]
        public void AccountNumber_ContainsPrefix()
        {
            Assert.Contains("ACC", _account.AccountNumber);
        }

        [TestMethod("Async deposit increases balance")]
        public async Task DepositAsync_ValidAmount_IncreasesBalance()
        {
            decimal initialBalance = _account.Balance;
            bool result = await _account.DepositAsync(250m);
            Assert.IsTrue(result);
            Assert.AreEqual(initialBalance + 250m, _account.Balance);
        }

        [TestMethod("Async withdraw decreases balance")]
        public async Task WithdrawAsync_ValidAmount_DecreasesBalance()
        {
            decimal initialBalance = _account.Balance;
            bool result = await _account.WithdrawAsync(150m);
            Assert.IsTrue(result);
            Assert.AreEqual(initialBalance - 150m, _account.Balance);
        }

        [TestMethod("Async withdraw with insufficient funds throws exception")]
        public async Task WithdrawAsync_InsufficientFunds_ThrowsException()
        {
            await Assert.ThrowsAsync<InvalidOperationException>(async () => 
                await _account.WithdrawAsync(5000m));
        }

        [TestMethod("Get balance asynchronously")]
        public async Task GetBalanceAsync_ReturnsCurrentBalance()
        {
            decimal balance = await _account.GetBalanceAsync();
            Assert.AreEqual(_account.Balance, balance);
            Assert.AreNotEqual(0m, balance);
        }

        [TestMethod("Get last transaction asynchronously")]
        public async Task GetLastTransactionAsync_ReturnsTransaction()
        {
            _account.Deposit(100m);
            Transaction? transaction = await _account.GetLastTransactionAsync();
            Assert.IsNotNull(transaction);
            Assert.IsTrue(transaction.Amount > 0);
        }

        [TestMethod("Async operation failure throws exception")]
        public async Task FailOperationAsync_ThrowsException()
        {
            await Assert.ThrowsAsync<InvalidOperationException>(async () => 
                await _account.FailOperationAsync());
        }

        [TestMethod("Different account instances are not same")]
        public void BankAccount_DifferentInstances_AreNotSame()
        {
            var account1 = new BankAccount("ACC001");
            var account2 = new BankAccount("ACC002");
            
            Assert.AreNotSame(account1, account2);
            Assert.AreNotEqual(account1.AccountNumber, account2.AccountNumber);
        }

        [TestMethod("Failing test for demonstration")]
        [Category("Demo")]
        [Priority(0)]
        [Author("Demo")]
        public void FailingTest_Demonstration()
        {
            _account.Deposit(100m);
            // Этот тест намеренно провалится - ожидаем баланс 2000, но будет 1100
            Assert.AreEqual(2000m, _account.Balance);
        }

        [TestMethod("Timeout attribute demonstration")]
        [Timeout(50)]
        [Category("Slow")]
        [Priority(0)]
        public async Task TimeoutAttribute_Demonstration()
        {
            await Task.Delay(200);
        }
    }
}
