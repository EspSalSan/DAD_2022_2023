using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

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
        private int balance;
        private bool isFrozen;
        private int totalSlots;   // The number of total slots elapsed since the beginning of the program
        private int currentSlot;  // The number of experienced slots (process may be frozen and not experience all slots)
        private int currentSequenceNumber;
        private readonly Dictionary<int,int> primaryPerSlot;

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

            this.balance = 0;
            this.isFrozen = false;
            this.totalSlots = 0;
            this.currentSlot = 0;
            this.currentSequenceNumber = 0;
            this.primaryPerSlot = new Dictionary<int,int>();
        }

        /*
         * At the start of every slot this function is called to "prepare the slot".
         * Updates process state (frozen or not).
         * If there is missing history regarding previous primaries, calls CompareAndSwap for each missing slot.
         * Electes a primary process for the current slot.
         * Does Cleanup if leader changes.
         */
        public void PrepareSlot()
        {
            /*
             * TODO: How to handle client requests (2PC, sequence numbers, etc)
             * IDEIAS:
             * somehow make process stop processing requeste
             * when the slot ends:
             * if there are requests pending (and is current leader), they go to the next slot
             * keep this requests as nonFinished
             * during the switch of slot:
             * if requests come, save then and dont process
             * (maybe) send them to the listOfPending (if the leader is another)
             */

            Monitor.Enter(this);
            if (this.totalSlots >= processFrozenPerSlot.Count)
            {
                Console.WriteLine("Slot duration ended but no more slots to process.");
                return;
            }

            // Switch process state
            this.isFrozen = this.processFrozenPerSlot[totalSlots];
            Console.WriteLine($"Process is now {(this.isFrozen ? "frozen" : "normal")}");

            this.totalSlots++;

            if (this.isFrozen)
            {
                Monitor.Exit(this);
                return;
            }

            // Verify if there is missing history
            while(this.currentSlot != this.totalSlots)
            {
                // Local slot counter
                this.currentSlot++;
                
                Console.WriteLine($"Preparing slot {this.currentSlot}...");
                
                DoCompareAndSwap(this.currentSlot);

                if (this.primaryPerSlot.Count > 1 && this.primaryPerSlot[this.currentSlot] != this.primaryPerSlot[this.currentSlot-1])
                {
                    Console.WriteLine("[NOT IMPLEMENTED] Leader has changed, starting cleanup...");
                    // TODO: Cleanup
                }

                Console.WriteLine("Preparation ended.");
            }
            Monitor.PulseAll(this);
            Monitor.Exit(this);
        }

        /*
         * Bank Service (Server) Implementation
         * Communication between BankClient and BankServer
         */

        public DepositReply DepositMoney(DepositRequest request)
        {
            Monitor.Enter(this);
            while (this.isFrozen)
            {
                Monitor.Wait(this);
            }
            
            Console.WriteLine($"Deposit request ({request.Value}) from {request.ClientId}");

            if (this.processId == this.primaryPerSlot[this.currentSlot])
            {
                Start2PC(ref request);  // TODO: Work in progress
            }

            DepositReply reply; 
            if (this.processId == this.primaryPerSlot[this.currentSlot])
            {
                reply = new DepositReply
                {
                    Balance = this.balance += request.Value,
                    Primary = this.primaryPerSlot[this.currentSlot] == this.processId,
                };
            }
            else
            {
                reply = new DepositReply
                {
                    Balance = this.balance,
                    Primary = this.primaryPerSlot[this.currentSlot] == this.processId,
                };
            }
            
            Monitor.Exit(this);
            return reply;
        }

        public WithdrawReply WithdrawMoney(WithdrawRequest request)
        {
            Monitor.Enter(this);
            while (this.isFrozen)
            {
                Monitor.Wait(this);  // wait until not frozen
            }
            
            Console.WriteLine($"Withdraw request ({request.Value}) from {request.ClientId}");

            if (this.processId == this.primaryPerSlot[this.currentSlot])
            {
                Start2PC(ref request);  // TODO: Work in progress
            }

            WithdrawReply reply;
            if (this.processId == this.primaryPerSlot[this.currentSlot])
            {
                reply = new WithdrawReply
                {
                    Value = request.Value > this.balance ? 0 : request.Value,
                    Balance = request.Value > this.balance ?  this.balance : (this.balance -= request.Value),
                    Primary = this.primaryPerSlot[this.currentSlot] == this.processId,
                };
            }
            else
            {
                reply = new WithdrawReply
                {
                    Value = request.Value,
                    Balance = this.balance,
                    Primary = this.primaryPerSlot[this.currentSlot] == this.processId,
                };
            }
            
            Monitor.Exit(this);
            return reply;
        }

        public ReadReply ReadBalance(ReadRequest request)
        {
            Monitor.Enter(this);
            while (this.isFrozen)
            {
                // wait until not frozen
                Monitor.Wait(this);
            }
            
            if (this.processId == this.primaryPerSlot[this.currentSlot])
            {
                //Start2PC(ref request); // TODO: Work in progress
            }

            Console.WriteLine($"Read request from {request.ClientId}");
            ReadReply reply = new ReadReply
            {
                Balance = balance,
                Primary = this.primaryPerSlot[this.currentSlot] == this.processId,
            };

            Monitor.Exit(this);
            return reply;
        }

        /*
         * Two Phase Commit Service (Server) Implementation
         * Communication between BankServer and BankServer
         * TODO: Work in progress
         */

        public TentativeReply Tentative(TentativeRequest request)
        {
            Console.WriteLine($"Tentative from {request.ProcessId} in slot {request.Slot} with sequence number {request.SequenceNumber}");

            bool acknowledge;
            // TODO: "Has not changed until the current slot"
            // This means that we have to verify every slot from
            // request.Slot to this.currentSlot and all of them have to be == request.ProcessId ?

            // Sender is the primary of the corresponding slot AND Sender is the current primary
            //if (this.primaryPerSlot[request.Slot] == request.ProcessId && this.primaryBankProcess == request.ProcessId)
            //    acknowledge = true;
            //else
            //    acknowledge = false;

            acknowledge = true;

            return new TentativeReply
            {
                Acknowledge = acknowledge,
            };
        }

        public CommitReply Commit(CommitRequest request)
        {
            Console.WriteLine($"Commit from {request.ProcessId} in slot {request.Slot} with sequence number {request.SequenceNumber} to {request.Request.Action}");

            switch (request.Request.Action)
            {
                case (ClientAction.Deposit):
                    this.balance += request.Request.Value;
                    break;

                case (ClientAction.Withdraw):
                    if (request.Request.Value <= this.balance)
                    {
                        this.balance -= request.Request.Value;
                    }
                    break;

                case (ClientAction.Read):
                    // TODO: Secondaries dont need to "do reads" ?
                    break;
            }

            return new CommitReply
            {
                // empty
            };
        }

        /*
         * Two Phase Commit Service (Client) Implementation
         * Communication between BankServer and BankServer
         * TODO: Work in progress
         */

        public void Start2PC<T>(ref T request)
        {
            // TODO: O QUE FAZER COM SEQUENCE NUMBER ASSOCIADO AO COMANDO ?
            // GUARDAR ?
            Console.WriteLine("Starting 2PC");
            int sequenceNumber = this.currentSequenceNumber;
            sequenceNumber++;

            bool success = SendTentativeRequest(sequenceNumber);

            if (success)
            {
                SendCommitRequest(ref request);
                this.currentSequenceNumber = sequenceNumber;
            }
            else
            {
                // TODO: ?
            }
        }

        public bool SendTentativeRequest(int sequenceNumber)
        {
            Console.WriteLine("Sending tentatives.");

            TentativeRequest tentativeRequest = new TentativeRequest
            {
                ProcessId = this.processId,
                Slot = this.currentSlot,
                SequenceNumber = sequenceNumber,
            };

            // Send request to all bank processes
            List<TentativeReply> tentativeReplies = new List<TentativeReply>();
            List<Task> tasks = new List<Task>();
            foreach (var host in this.bankHosts)
            {
                if (host.Key == this.primaryPerSlot[this.currentSlot])
                {
                    continue;
                }
                Task t = Task.Run(() =>
                {
                    try
                    {
                        lock (tentativeReplies)
                        {
                            TentativeReply tentativeReply = host.Value.Tentative(tentativeRequest);
                            tentativeReplies.Add(tentativeReply);
                            Console.WriteLine(tentativeReplies.Count);
                        }
                    }
                    catch (Grpc.Core.RpcException e)
                    {
                        Console.WriteLine(e.Status);
                    }
                    return Task.CompletedTask;
                });
                tasks.Add(t);
            }

            // Wait for a majority of responses
            int majority = (this.bankHosts.Count - 1) / 2 + 1;
            for (int i = 0; i < majority; i++)
                tasks.RemoveAt(Task.WaitAny(tasks.ToArray()));

            Console.WriteLine($"Sent {tentativeReplies.Count} tentative requests");

            // Verify if majority acknowledges
            int acknowledges = 0;
            foreach (TentativeReply reply in tentativeReplies)
            {
                if (reply.Acknowledge)
                    acknowledges++;
            }

            return acknowledges >= majority;
        }

        public void SendCommitRequest<T>(ref T request)
        {
            Console.WriteLine("Sending commits.");
            // TODO: Better way of doing this?
            // Tambem se podia passsar string comando e int value
            // e fazer switch com strings em vez de com tipos
            ClientRequest clientRequest;
            if (request.GetType() == typeof(DepositRequest))
            {
                DepositRequest r = (DepositRequest)Convert.ChangeType(request, typeof(DepositRequest));
                clientRequest = new ClientRequest
                {
                    Action = ClientAction.Deposit,
                    Value = r.Value,
                };
            }
            else if (request.GetType() == typeof(WithdrawRequest))
            {
                WithdrawRequest r = (WithdrawRequest)Convert.ChangeType(request, typeof(WithdrawRequest));
                clientRequest = new ClientRequest
                {
                    Action = ClientAction.Withdraw,
                    Value = r.Value,
                };
            }
            else
            {
                clientRequest = new ClientRequest
                {
                    Action = ClientAction.Read,
                    Value = 0,
                };
            }

            CommitRequest commitRequest = new CommitRequest
            {
                ProcessId = this.processId,
                Slot = this.currentSlot,
                SequenceNumber = currentSequenceNumber,
                Request = clientRequest,
            };

            // Send request to all bank processes
            List<Task> tasks = new List<Task>();
            List<CommitReply> replies = new List<CommitReply>();
            foreach (var host in this.bankHosts)
            {
                if (host.Key == this.primaryPerSlot[this.currentSlot])
                {
                    continue;
                }
                Task t = Task.Run(() =>
                {
                    try
                    {
                        CommitReply commitReply = host.Value.Commit(commitRequest);
                    }
                    catch (Grpc.Core.RpcException e)
                    {
                        Console.WriteLine(e.Status);
                    }
                    return Task.CompletedTask;
                });
                tasks.Add(t);
            }
        }

        /*
         * Cleanup Service (Server) Implementation
         * Communication between BankServer and BankServer
         * TODO: Work in progress
         */

        public ListPendingRequestsReply Cleanup(ListPendingRequestsRequest request)
        {

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
            
            return new ListPendingRequestsReply
            {
            };
        }

        /*
         * Cleanup Service (Client) Implementation
         * Communication between BankServer and BankServer
         * TODO: Work in progress
         */

        public void SendCleanupRequest()
        {
        }

        /*
         * Compare And Swap Service (Client) Implementation
         * Communication between BankServer and BankServer
         */
        public void DoCompareAndSwap(int slot)
        {
            // Choose new primary process
            int primary = int.MaxValue;
            foreach (KeyValuePair<int, bool> process in this.processesSuspectedPerSlot[slot - 1])
            {
                // Bank process that is not suspected and has the lowest id
                if (!process.Value && process.Key < primary && this.bankHosts.ContainsKey(process.Key))
                    primary = process.Key;
            }

            if (primary == int.MaxValue)
            {
                // TODO: Guardar como lider do slot N o lider do slot N-1 ?
                Console.WriteLine("No process is valid for leader election.");
                Console.WriteLine("No progress is going to be made in this slot.");
                return;
            }

            int electedPrimary = SendCompareAndSwapRequest(primary, slot);

            if (electedPrimary == -1)
            {
                // TODO: something went wrong at the compare and swap service
            }

            this.primaryPerSlot.Add(slot, electedPrimary);
            Monitor.PulseAll(this);

            Console.WriteLine($"-----------------------------\r\nProcess {electedPrimary} is the primary for slot {slot}.\r\n-----------------------------");
        }
        
        public int SendCompareAndSwapRequest(int primary, int slot)
        {
            int compareAndSwapReplyValue = -1;

            CompareAndSwapRequest compareAndSwapRequest = new CompareAndSwapRequest
            {
                Slot = slot,
                Invalue = primary,
            };

            Console.WriteLine($"Trying to elect process {primary} as primary for slot {slot}.");

            // Send request to all boney processes
            List<Task> tasks = new List<Task>();
            foreach (var host in this.boneyHosts)
            {
                Task t = Task.Run(() =>
                {
                    try
                    {
                        CompareAndSwapReply compareAndSwapReply = host.Value.CompareAndSwap(compareAndSwapRequest);
                        compareAndSwapReplyValue = compareAndSwapReply.Outvalue;
                    }
                    catch (Grpc.Core.RpcException e)
                    {
                        Console.WriteLine(e.Status);
                    }
                    return Task.CompletedTask;
                });
                tasks.Add(t);
            }

            // Wait for a majority of responses
            for (int i = 0; i < this.boneyHosts.Count / 2 + 1; i++)
                tasks.RemoveAt(Task.WaitAny(tasks.ToArray()));

            return compareAndSwapReplyValue;
        }
    }
}
