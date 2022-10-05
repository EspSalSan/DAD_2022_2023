using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace BankServer.Services
{
    public class ServerService
    {
        // Config file variables
        private readonly int processId;
        private readonly List<Dictionary<int, bool>> processesSuspectedPerSlot;
        private readonly List<bool> processFrozenPerSlot;
        private readonly Dictionary<int, TwoPhaseCommit.TwoPhaseCommitClient> bankHosts;
        private readonly Dictionary<int, CompareAndSwap.CompareAndSwapClient> boneyHosts;

        // Changing variables
        private int currentSlot;
        private int currentBankLeader;
        private bool isFrozen;
        private int balance;
        private Dictionary<int, int> lastKnownSequenceNumber;

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
             */

            if(this.currentSlot >= processFrozenPerSlot.Count){
                Console.WriteLine("No more slots to process.");
                Console.WriteLine("Aborting...");
                return;
            }

            this.currentSlot += 1;

            Console.WriteLine($"Preparing slot {this.currentSlot}");

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
                if(!process.Value && process.Key < leader && this.bankHosts.ContainsKey(process.Key))
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

            Console.WriteLine($"Trying to elect process {leader} as leader.");

            // Save old leader to know if cleanup is needed
            int oldBankLeader = this.currentBankLeader;
            // TODO se ele suspeita que um boney está frozen envia pedido à mesma ?
            // TODO deviamos recolher as respostas todas e confirmar que sao iguais ? (pelo paxos deviam)
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

            Console.WriteLine($"Process {this.currentBankLeader} is the new leader.");

            // Start Cleanup (if necessary)
            if(this.currentBankLeader != oldBankLeader && this.currentBankLeader == this.processId)
            {
                // DO CLEANUP
            }


            // só faz cleanup o processo que se tornou lider e antes nao era 
            // do cleanup();
            // Send 'ListPendingRequests(LastKnownSequenceNumber)' to all banks 
            // Nodes only reply to this request after they have moved to the new slot and the request comes from the primary assigned for that slot
            // Wait for a majority of replies
            // Collect sequence numbers from previous primaries that have not been commited yet
            // propose and commit those sequence numbers
            // start assigning new sequence numbers to commands that dont have

            // em portugues:
            // O (novo) Lider envia o numero de sequencia mais alto que foi committed a todas as replicas
            // Recebe vários numeros de sequencia que foram proposed mas não committed
            // Dá propose e commit desses numeros de sequencia
            // Quando acabar, começa a dar assign de numeros de sequencia a comandos que ainda nao tenham

            // Coisas que 'parecem' ser preciso guardar para cada banco (por slot)
            /*
             * Comandos sem sequenceNumber
             * Comandos com sequenceNumber
             * Comandos proposed
             * Comandos commited
             */

            Console.WriteLine("Preparation ended.");
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
