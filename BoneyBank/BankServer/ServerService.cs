using System;
using System.Collections.Generic;
using System.Text;

namespace BankServer
{
    public class ServerService
    {
        private int processId;
        private int balance;
        public ServerService(int processId)
        {
            this.balance = 0;
            this.processId = processId;
        }

        /*
         * Bank Service Implementation
         * Communication between BankClient and BankServer
         */

        public WithdrawReply WithdrawMoney(WithdrawRequest request)
        {
            lock (this)
            {
                Console.WriteLine($"Withdraw: {request.Value}");

                this.balance -= request.Value;

                return new WithdrawReply
                {
                    Value = request.Value,
                    Balance = this.balance
                };
            }
        }

        public DepositReply DepositMoney(DepositRequest request)
        {
            lock (this)
            {
                Console.WriteLine($"Deposit: {request.Value}");
                this.balance += request.Value;
                return new DepositReply
                {
                    Balance = this.balance
                };
            }
        }

        public ReadReply ReadBalance(ReadRequest request)
        {
            Console.WriteLine($"Read: {this.balance}");
            return new ReadReply
            {
                Balance = this.balance
            };
        }
    }
}
