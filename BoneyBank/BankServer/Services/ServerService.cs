using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace BankServer.Services
{
    public class ServerService
    {
        // From config file
        private int processId;
        private List<Dictionary<int, bool>> processesSuspectedPerSlot;
        private List<bool> processFrozenPerSlot;
        private Dictionary<int, TwoPhaseCommit.TwoPhaseCommitClient> bankHosts;
        private Dictionary<int, CompareAndSwap.CompareAndSwapClient> boneyHosts;

        // Everything else
        private int currentSlot;
        private int currentBankLeader;
        private bool isFrozen;
        private int balance;
        private ConcurrentDictionary<int, int> lastKnownSequenceNumber;

        public ServerService(
            int processId, 
            List<bool> processFrozenPerSlot, 
            List<Dictionary<int, bool>> processesSuspectedPerSlot,
            Dictionary<int, TwoPhaseCommit.TwoPhaseCommitClient> bankHosts,
            Dictionary<int, CompareAndSwap.CompareAndSwapClient> boneyHosts
            )
        {
            this.processId = processId;
            this.processesSuspectedPerSlot = processesSuspectedPerSlot;
            this.processFrozenPerSlot = processFrozenPerSlot;
            this.bankHosts = bankHosts;
            this.boneyHosts = boneyHosts;

            this.currentSlot = 0;
            this.currentBankLeader = 0;
            this.isFrozen = false;
            this.balance = 0;
        }

        /*
        * Se guardarmos a informacao dos slots no ServerService
        *  e.g.: current slot, last pending request,etc
        *  Então as funcoes que chamamos aqui tambem têm de estar no ServerService
        *  Não sei se é critico misturar funcoes de Servidor com funcoes de Cliente
        *  provavelmente não
        */

        public void PrepareSlot()
        {
            /*
             * 
             * somehow make process stop processing requeste
             * when the slot ends:
             * if there are requests pending (and is current leader), they go to the next slot
             * keep this requests as nonFinished
             * during the switch of slot:
             * if requests come, save then and dont process
             * (maybe) send them to the listOfPending (if the leader is another)
             * 
             * 
             */



            this.currentSlot += 1;

            Console.WriteLine($"Starting slot {this.currentSlot}");

            // Switch process state
            this.isFrozen = this.processFrozenPerSlot[currentSlot - 1];

            // Select new leader
            // tem bug
            // neste dicionario estao todos os processos mas ele quer apenas ver os processos do banco (e nao do boney)
            Dictionary<int, bool> processesSuspected = this.processesSuspectedPerSlot[currentSlot - 1];
            int leader = int.MaxValue;
            foreach (KeyValuePair<int, bool> process in processesSuspected)
            {
                // Process that is not suspected and has the lowest id
                if(!process.Value && process.Key < leader)
                {
                    leader = process.Key;
                }
            }

            if(leader == int.MaxValue)
            {
                // something went wrong, all processes are frozen
                // abort ? stall ?
            }

            // Start Compare and Swap
            CompareAndSwapRequest compareAndSwapRequest = new CompareAndSwapRequest
            {
                Invalue = leader,
                Slot = currentSlot,
            };

            // Save old leader to know if cleanup is needed
            int oldBankLeader = this.currentBankLeader;

            // Send request to all bank processes
            foreach (var entry in this.boneyHosts)
            {
                try
                {
                    CompareAndSwapReply compareAndSwapReply = entry.Value.CompareAndSwap(compareAndSwapRequest);
                    this.currentBankLeader = compareAndSwapReply.Outvalue;
                    Console.WriteLine($"Compare and Swap result: {this.currentBankLeader}");
                }
                catch (Grpc.Core.RpcException e)
                {
                    Console.WriteLine(e.Status);
                }
            }

            // Start Cleanup (if necessary)
            if(this.currentBankLeader == oldBankLeader)
            {
                // same leader
                // do we need to do something?
            }

            // leader changed
            // do cleanup();
            // Send 'ListPendingRequests(LastKnownSequenceNumber)' 
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
