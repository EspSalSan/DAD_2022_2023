using BankServer.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BankServer.Services
{
    public class ServerService
    {
        // Config file variables
        private readonly int processId;
        private readonly List<bool> processFrozenPerSlot;
        private readonly List<Dictionary<int, bool>> processesSuspectedPerSlot;
        private readonly Dictionary<int, TwoPhaseCommit.TwoPhaseCommitClient> bankHosts;
        private readonly Dictionary<int, CompareAndSwap.CompareAndSwapClient> boneyHosts;

        // Paxos variables
        private bool isFrozen;
        private int totalSlots;   // The number of total slots elapsed since the beginning of the program
        private int currentSlot;  // The number of experienced slots (process may be frozen and not experience all slots)
        private readonly Dictionary<int,int> primaryPerSlot;

        // Replication variables
        private int balance;
        private bool isCleanning;
        private int currentSequenceNumber;
        // TODO: probably devem ser listas e queues e nao dict
        // key (clientId, clientSequenceNumber)
        private readonly Dictionary<(int, int), ClientCommand> receivedCommands;
        private readonly Dictionary<(int, int), ClientCommand> tentativeCommands;
        private readonly Dictionary<(int, int), ClientCommand> committedCommands;

        public ServerService(
            int processId,
            List<bool> processFrozenPerSlot,
            List<Dictionary<int, bool>> processesSuspectedPerSlot,
            Dictionary<int, TwoPhaseCommit.TwoPhaseCommitClient> bankHosts,
            Dictionary<int, CompareAndSwap.CompareAndSwapClient> boneyHosts
            )
        {
            this.processId = processId;
            this.bankHosts = bankHosts;
            this.boneyHosts = boneyHosts;
            this.processFrozenPerSlot = processFrozenPerSlot;
            this.processesSuspectedPerSlot = processesSuspectedPerSlot;

            this.balance = 0;
            this.isFrozen = false;
            this.totalSlots = 0;
            this.currentSlot = 0;
            this.currentSequenceNumber = 0;
            this.primaryPerSlot = new Dictionary<int,int>();

            this.isCleanning = false;
            this.receivedCommands = new Dictionary<(int, int), ClientCommand>();
            this.tentativeCommands = new Dictionary<(int, int), ClientCommand>();
            this.committedCommands = new Dictionary<(int, int), ClientCommand>();
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

                if (this.primaryPerSlot.Count > 1 && this.primaryPerSlot[this.currentSlot] != this.primaryPerSlot[this.currentSlot - 1] && this.primaryPerSlot[this.currentSlot] == this.processId)
                {
                    Console.WriteLine($"Leader changed from {this.primaryPerSlot[this.currentSlot - 1]} to {this.primaryPerSlot[this.currentSlot]}");
                    //DoCleanup();
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

            ClientCommand command = new ClientCommand(
                this.currentSlot,
                request.ClientId,
                request.ClientSequenceNumber,
                -1,
                CommandType.Deposit,
                request.Value
            );

            while (this.isFrozen)
            {
                Monitor.Wait(this);
            }
            
            Console.WriteLine($"Deposit request ({request.Value}) from {request.ClientId}");

            this.receivedCommands.Add(
               (command.ClientId, command.ClientSequenceNumber),
               command
            );

            // If leader for the current slot, start 2PC
            if (this.processId == this.primaryPerSlot[this.currentSlot] && command.Slot == this.currentSlot)
                Do2PC(command);

            // Wait for command to be committed (and applied) before sending response
            while (!this.committedCommands.ContainsKey((command.ClientId, command.ClientSequenceNumber)))
            {
                Monitor.Wait(this);
            }

            DepositReply reply = new DepositReply
            {
                Balance = this.balance,
                Primary = this.primaryPerSlot[this.currentSlot] == this.processId,
            };
            
            Monitor.Exit(this);
            return reply; 
        }

        public WithdrawReply WithdrawMoney(WithdrawRequest request)
        {
            Monitor.Enter(this);

            ClientCommand command = new ClientCommand(
                this.currentSlot,
                request.ClientId,
                request.ClientSequenceNumber,
                -1,
                CommandType.Withdraw,
                request.Value
            );

            while (this.isFrozen)
            {
                Monitor.Wait(this);  // wait until not frozen
            }
            
            Console.WriteLine($"Withdraw request ({request.Value}) from {request.ClientId}");

            this.receivedCommands.Add(
               (command.ClientId, command.ClientSequenceNumber),
               command
            );

            // Read current balance to latter verify if withdraw as succesful
            int lastBalance = this.balance;

            // If leader for the current slot, start 2PC
            if (this.processId == this.primaryPerSlot[this.currentSlot] && command.Slot == this.currentSlot)
                Do2PC(command);

            // Wait for command to be committed (and applied) before sending response
            while (!this.committedCommands.ContainsKey((command.ClientId, command.ClientSequenceNumber)))
            {
                Monitor.Wait(this);
            }

            WithdrawReply reply = new WithdrawReply
            {
                Value = lastBalance == this.balance ? 0 :request.Value,
                Balance = this.balance,
                Primary = this.primaryPerSlot[this.currentSlot] == this.processId,
            };
  
            Monitor.Exit(this);
            return reply;
        }

        public ReadReply ReadBalance(ReadRequest request)
        {
            Monitor.Enter(this);
            while (this.isFrozen)
            {
                Monitor.Wait(this);                 // wait until not frozen
            }

            Console.WriteLine($"Read request from {request.ClientId}");
            
            // Para prevenir reads antigos deviamos confirmar se todos os comandos
            // deste cliente já foram executados OU fazer 2PC deles

            ReadReply reply = new ReadReply
            {
                Balance = balance,
                Primary = this.primaryPerSlot[this.currentSlot] == this.processId,
            };

            Monitor.Exit(this);
            return reply;
        }

        /*
         * Compare And Swap Service (Client) Implementation
         * Communication between BankServer and BankServer
         */
        public void DoCompareAndSwap(int slot)
        {
            // Choose primary process
            int primary = int.MaxValue;
            if (slot > 1 && !this.processesSuspectedPerSlot[slot-1][this.primaryPerSlot[slot - 1]])
            {
                primary = this.primaryPerSlot[slot - 1];
            }
            else
            {
                foreach (KeyValuePair<int, bool> process in this.processesSuspectedPerSlot[slot - 1])
                {
                    // Bank process that is not suspected and has the lowest id
                    if (!process.Value && process.Key < primary && this.bankHosts.ContainsKey(process.Key))
                        primary = process.Key;
                }
            } 
            

            if (primary == int.MaxValue)
            {
                // TODO: Guardar ,como lider do slot N, o lider do slot N-1 ?
                Console.WriteLine("No process is valid for leader election.");
                Console.WriteLine("No progress is going to be made in this slot.");
                return;
            }

            int electedPrimary = SendCompareAndSwapRequest(primary, slot);

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

        /*
         * Two Phase Commit Service (Server) Implementation
         * Communication between BankServer and BankServer
         * TODO: Work in progress
         */

        public TentativeReply Tentative(TentativeRequest request)
        {
            try
            {
                Monitor.Enter(this);

                while (this.isFrozen)
                {
                    Console.WriteLine("Sleeping...");
                    Monitor.Wait(this);
                    Console.WriteLine("Waking up...");
                }

                Console.WriteLine($"Tentative from {request.ProcessId} in slot {request.Command.Slot} with sequence number {request.Command.SequenceNumber}");

                bool ack = true;
                // Sender is primary of the slot
                if (this.primaryPerSlot[request.Command.Slot] == request.ProcessId)
                {
                    // Primary hasn't changed since the command was sent
                    foreach (KeyValuePair<int, int> primary in this.primaryPerSlot)
                    {
                        if (primary.Key > request.Command.Slot && primary.Value != this.primaryPerSlot[request.Command.Slot])
                        {
                            ack = false;
                            break;
                        }
                    }
                }
                else
                    ack = false;

                if (ack)
                    // Add to tentative commands
                    this.tentativeCommands.Add(
                        (request.Command.ClientId, request.Command.ClientSequenceNumber),
                        new ClientCommand(
                            request.Command.Slot,
                            request.Command.ClientId,
                            request.Command.ClientSequenceNumber,
                            request.Command.SequenceNumber,
                            (CommandType)request.Command.Type,
                            request.Command.Value
                        )
                    );

                Monitor.Exit(this);
                return new TentativeReply
                {
                    Acknowledge = ack,
                };
            } catch (Exception e)
            {
                Console.WriteLine(e.StackTrace);
                return null;
            }
           
        }

        public CommitReply Commit(CommitRequest request)
        {
            Console.WriteLine($"Commit from {request.ProcessId} in slot {request.Command.Slot} with sequence number {request.Command.SequenceNumber} to {request.Command.Type}");

            Monitor.Enter(this);

            while (this.isFrozen)
            {
                Monitor.Wait(this);
            }

            // TODO: should verify if command with lower sequence number was already committed             

            switch (request.Command.Type)
            {
                case (Type.Deposit):
                    this.balance += request.Command.Value;
                    break;
                    
                case (Type.Withdraw):
                    if (request.Command.Value <= this.balance)
                    {
                        this.balance -= request.Command.Value;
                    }
                    break;
            }


            // TODO: Existe a possibilidade de o comando nunca ter estado tentative ?

            // Transfer command from tentative to committed
            (int, int) key = (request.Command.ClientId, request.Command.ClientSequenceNumber);
            ClientCommand command = this.tentativeCommands.GetValueOrDefault(key);
            this.tentativeCommands.Remove(key);
            this.committedCommands.Add(key, command);
    
            Monitor.PulseAll(this);  
            Monitor.Exit(this);

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

        public void Do2PC(ClientCommand command)
        {
            Console.WriteLine("Starting 2PC");
            
            int sequenceNumber = this.currentSequenceNumber;
            sequenceNumber++;

            if (SendTentativeRequest(sequenceNumber, command))
            {
                SendCommitRequest(sequenceNumber, command);
                this.currentSequenceNumber = sequenceNumber;
            } else
            {
                Console.WriteLine("Tentative rejected");
            }
            //  TODO: If tentative fails, idk

            Console.WriteLine("Finished 2PC");
        }
        
        public bool SendTentativeRequest(int sequenceNumber, ClientCommand command)
        {
            Console.WriteLine("Sending tentatives.");

            TentativeRequest tentativeRequest = new TentativeRequest
            {
                ProcessId = this.processId,
                Command = new Command
                {
                    Slot = command.Slot,
                    ClientId = command.ClientId,
                    ClientSequenceNumber = command.ClientSequenceNumber,
                    SequenceNumber = sequenceNumber,
                    Type = (Type)command.Type,
                    Value = command.Value,
                }
            };

            // Send request to all bank processes
            List<TentativeReply> tentativeReplies = new List<TentativeReply>();
            List<Task> tasks = new List<Task>();
            foreach (var host in this.bankHosts)
            {
                Task t = Task.Run(() =>
                {
                    try
                    {
                        TentativeReply tentativeReply = host.Value.Tentative(tentativeRequest);
                        tentativeReplies.Add(tentativeReply);
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
            int majority = this.bankHosts.Count / 2 + 1;
            for (int i = 0; i < majority; i++)
                tasks.RemoveAt(Task.WaitAny(tasks.ToArray()));

            // Verify if majority acknowledges
            int acknowledges = 0;
            foreach (TentativeReply reply in tentativeReplies)
            {
                if (reply.Acknowledge)
                    acknowledges++;
            }
            return acknowledges >= majority;
        }

        public void SendCommitRequest(int sequenceNumber, ClientCommand command)
        {
            Console.WriteLine("Sending commits.");

            CommitRequest commitRequest = new CommitRequest
            {
                ProcessId = this.processId,
                Command = new Command
                {
                    Slot = command.Slot,
                    ClientId = command.ClientId,
                    ClientSequenceNumber = command.ClientSequenceNumber,
                    SequenceNumber = sequenceNumber,
                    Type = (Type)command.Type,
                    Value = command.Value,
                }
            };

            // Send request to all bank processes
            List<CommitReply> replies = new List<CommitReply>();
            foreach (var host in this.bankHosts)
            {
                Task t = Task.Run(() =>
                {
                    try
                    {
                        CommitReply commitReply = host.Value.Commit(commitRequest);
                        Console.WriteLine("Received commit reply");                    }
                    catch (Grpc.Core.RpcException e)
                    {
                        Console.WriteLine(e.Status);
                    }
                    return Task.CompletedTask;
                });
            }            
        }

        /*
         * Cleanup Service (Server) Implementation
         * Communication between BankServer and BankServer
         * TODO: Work in progress
         */

        public ListPendingRequestsReply ListPendingRequests(ListPendingRequestsRequest request)
        {
            // TODO: why do we send the lastKnownSequenceNumber ?
            
            
            // Transform every tentativeCommand into a Command from grpc
            List<Command> tentativeCommands = new List<Command>();
            foreach (var tentativeCommand in this.tentativeCommands)
            {
                tentativeCommands.Add(new Command
                {
                    Slot = tentativeCommand.Value.Slot,
                    ClientId = tentativeCommand.Value.ClientId,
                    ClientSequenceNumber = tentativeCommand.Value.ClientSequenceNumber,
                    SequenceNumber = tentativeCommand.Value.SequenceNumber,
                    Type = (Type)tentativeCommand.Value.Type,
                    Value = tentativeCommand.Value.Value,
                });
            }

            return new ListPendingRequestsReply
            {
                Commands = { tentativeCommands },
            };
        }

        /*
         * Cleanup Service (Client) Implementation
         * Communication between BankServer and BankServer
         * TODO: Work in progress
         */
        
        public void DoCleanup()
        {
            Console.WriteLine("Starting cleanup");
            this.isCleanning = true;

            List<ClientCommand> clientCommands = SendListPendingRequestsRequest();



            // TODO do 2PC for every command in clientCommands with correct sequence number
            


            this.isCleanning = false;
        }

        public List<ClientCommand> SendListPendingRequestsRequest()
        {
            Console.WriteLine("Sending list pending requests.");

            // TODO: o currentSequenceNumber pode ser diferente do comando mais recente committed
            // (pode ja ter enviado commits mas ainda nao ter recebido)
            // qual devemos enviar ?
            ListPendingRequestsRequest request = new ListPendingRequestsRequest
            {
                LastKnownSequenceNumber = this.currentSequenceNumber,
            };

            // Send request to all bank processes
            List<ListPendingRequestsReply> replies = new List<ListPendingRequestsReply>();
            List<Task> tasks = new List<Task>();
            foreach (var host in this.bankHosts)
            {
                Task t = Task.Run(() =>
                {
                    try
                    {
                        ListPendingRequestsReply reply = host.Value.ListPendingRequests(request);
                        replies.Add(reply);
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
            int majority = this.bankHosts.Count / 2 + 1;
            for (int i = 0; i < majority; i++)
                tasks.RemoveAt(Task.WaitAny(tasks.ToArray()));

            // Merge all commands (that have been proposed but may not have been committed)
            // Without duplicates
            List<ClientCommand> commands = new List<ClientCommand>();
            foreach (ListPendingRequestsReply reply in replies)
            {
                foreach (Command command in reply.Commands)
                {
                    // Two commands are the same if they have the same clientId and clientSequenceNumber
                    if (!commands.Any(c => c.ClientId == command.ClientId && c.ClientSequenceNumber == command.ClientSequenceNumber))
                        // transform command from grpc to ClientCommand (not sure if necessary)
                        commands.Add(new ClientCommand
                        (
                            command.Slot,
                            command.ClientId,
                            command.ClientSequenceNumber,
                            command.SequenceNumber,
                            (CommandType)command.Type,
                            command.Value
                        ));
                }
            }

            // Sort commands increasingly by sequence number, if the same sort by decreasingly slot
            commands.Sort((c1, c2) =>
            {
                if (c1.SequenceNumber == c2.SequenceNumber)
                    return c2.Slot.CompareTo(c1.Slot);
                return c1.SequenceNumber.CompareTo(c2.SequenceNumber);
            });

            Console.WriteLine("Command count =" + commands.Count);
            // Print all commands form commands list, printing the sequence number and slot
            foreach (ClientCommand command in commands)
            {
                Console.WriteLine("Command: " + command.ClientId + " " + command.ClientSequenceNumber + " " + command.Slot);
            }

            return commands;
        }
    }
}
