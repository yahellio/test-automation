using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace TargetProject
{
    public class BankAccount
    {
        private decimal _balance;
        private readonly string _accountNumber;
        private readonly List<Transaction> _transactions;
        private bool _isLocked;

        public BankAccount(string accountNumber, decimal initialBalance = 0)
        {
            if (string.IsNullOrEmpty(accountNumber))
                throw new ArgumentException("Account number cannot be empty");
            
            _accountNumber = accountNumber;
            _balance = initialBalance;
            _transactions = new List<Transaction>();
            _isLocked = false;
        }

        public decimal Balance => _balance;
        public string AccountNumber => _accountNumber;
        public bool IsLocked => _isLocked;
        public int TransactionCount => _transactions.Count;

        public void Deposit(decimal amount)
        {
            if (_isLocked)
                throw new InvalidOperationException("Account is locked");
            
            if (amount <= 0)
                throw new ArgumentException("Deposit amount must be positive");

            _balance += amount;
            _transactions.Add(new Transaction(TransactionType.Deposit, amount, _balance));
        }

        public void Withdraw(decimal amount)
        {
            if (_isLocked)
                throw new InvalidOperationException("Account is locked");
            
            if (amount <= 0)
                throw new ArgumentException("Withdrawal amount must be positive");

            if (_balance < amount)
                throw new InvalidOperationException("Insufficient funds");

            _balance -= amount;
            _transactions.Add(new Transaction(TransactionType.Withdrawal, amount, _balance));
        }

        public async Task<bool> DepositAsync(decimal amount)
        {
            await Task.Delay(50);
            
            if (_isLocked)
                throw new InvalidOperationException("Account is locked");
            
            if (amount <= 0)
                throw new ArgumentException("Deposit amount must be positive");

            _balance += amount;
            _transactions.Add(new Transaction(TransactionType.Deposit, amount, _balance));
            return true;
        }

        public async Task<bool> WithdrawAsync(decimal amount)
        {
            await Task.Delay(50);
            
            if (_isLocked)
                throw new InvalidOperationException("Account is locked");
            
            if (amount <= 0)
                throw new ArgumentException("Withdrawal amount must be positive");

            if (_balance < amount)
                throw new InvalidOperationException("Insufficient funds");

            _balance -= amount;
            _transactions.Add(new Transaction(TransactionType.Withdrawal, amount, _balance));
            return true;
        }

        public async Task<decimal> GetBalanceAsync()
        {
            await Task.Delay(30);
            return _balance;
        }

        public async Task<Transaction?> GetLastTransactionAsync()
        {
            await Task.Delay(20);
            return _transactions.LastOrDefault();
        }

        public async Task FailOperationAsync()
        {
            await Task.Delay(30);
            throw new InvalidOperationException("Operation failed");
        }

        public void Lock()
        {
            _isLocked = true;
        }

        public void Unlock()
        {
            _isLocked = false;
        }

        public Transaction? GetLastTransaction()
        {
            return _transactions.LastOrDefault();
        }

        public bool HasTransactionHistory()
        {
            return _transactions.Count > 0;
        }
    }

    public enum TransactionType
    {
        Deposit,
        Withdrawal
    }

    public class Transaction
    {
        public TransactionType Type { get; }
        public decimal Amount { get; }
        public decimal BalanceAfter { get; }
        public DateTime Timestamp { get; }

        public Transaction(TransactionType type, decimal amount, decimal balanceAfter)
        {
            Type = type;
            Amount = amount;
            BalanceAfter = balanceAfter;
            Timestamp = DateTime.Now;
        }
    }
}
