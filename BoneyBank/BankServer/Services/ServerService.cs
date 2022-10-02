using System;
using System.Collections.Generic;
using System.Text;

namespace BankServer.Services
{
    public class ServerService
    {
        private int processId;
        private int balance;
        public ServerService(int processId)
        {
            balance = 0;
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

                balance -= request.Value;

                return new WithdrawReply
                {
                    Value = request.Value,
                    Balance = balance
                };
            }
        }

        public DepositReply DepositMoney(DepositRequest request)
        {
            lock (this)
            {
                Console.WriteLine($"Deposit: {request.Value}");
                balance += request.Value;
                return new DepositReply
                {
                    Balance = balance
                };
            }
        }

        public ReadReply ReadBalance(ReadRequest request)
        {
            Console.WriteLine($"Read: {balance}");
            return new ReadReply
            {
                Balance = balance
            };
        }
    }
}
