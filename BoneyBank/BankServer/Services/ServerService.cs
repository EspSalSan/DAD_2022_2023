using System;
using System.Collections.Concurrent;

namespace BankServer.Services
{
    public class ServerService
    {
        private int processId;
        private int currentSlot;
        private int balance;
        private ConcurrentDictionary<int, int> lastKnownSequenceNumber;
        
        public ServerService(int processId)
        {
            this.processId = processId;
            this.balance = 0;
            this.currentSlot = 0;
        }

        /*
         * Bank Service (Server) Implementation
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
            // lock for read?
            Console.WriteLine($"Read: {balance}");
            return new ReadReply
            {
                Balance = balance
            };
        }

        /*
         * Two Phase Commit Service (Client/Server) Implementation
         * Communication between BankServer and BankServer
         */

        public TentativeReply Tentative(TentativeRequest request)
        {
            // TODO
            return new TentativeReply
            {

            };
        }

        public  CommitReply Commit(CommitRequest request)
        {
            // TODO
            return new CommitReply
            {

            };
        }

        /*
         * Cleanup Service (Client/Server) Implementation
         * Communication between BankServer and BankServer
         */

        public ListPendingRequestsReply Cleanup(ListPendingRequestsRequest request)
        {
            // TODO
            return new ListPendingRequestsReply
            {

            };
        }

        /*
         * Compare And Swap Service (Client) Implementation
         * Communication between BankServer and BankServer
         */

        

    }
}
