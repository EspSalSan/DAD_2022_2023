using BankServer.Domain;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
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
        private decimal balance;
        private bool isCleanning;
        private int currentSequenceNumber; 
        private readonly Dictionary<(int, int), ClientCommand> tentativeCommands; // key: (clientId, clientSequenceNumber)
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
            Monitor.Enter(this);
            
            // End of slots
            if (this.totalSlots >= processFrozenPerSlot.Count)
            {
                Console.WriteLine("Slot duration ended but no more slots to process.");
                return;
            }

            Console.WriteLine("Preparing new slot(s) -----------------------");

            // Switch process state
            this.isFrozen = this.processFrozenPerSlot[totalSlots];
            Console.WriteLine($"Process is now {(this.isFrozen ? "frozen" : "normal")}");

            // Global slot counter
            this.totalSlots++;

            // If process is frozen, it does not experience the slot
            if (this.isFrozen)
            {
                Console.WriteLine("Ending preparation -----------------------");
                Monitor.Exit(this);
                return;
            }

            // Verify if there is missing history
            while(this.currentSlot != this.totalSlots)
            {
                // Local slot counter
                this.currentSlot++;
                
                Console.WriteLine($"Preparing slot {this.currentSlot}...");
                try
                {
                    DoCompareAndSwap(this.currentSlot);
                } catch (Exception e)
                {
                    Console.WriteLine(e.StackTrace);
                    Console.WriteLine("--------------------------------------------");
                }
                // Elect a primary process for the current slot
                

                // If leader changed, do cleanup
                if (
                    this.primaryPerSlot.Count > 1 && 
                    this.primaryPerSlot[this.currentSlot] != this.primaryPerSlot[this.currentSlot - 1] && 
                    this.primaryPerSlot[this.currentSlot] == this.processId
                    )
                {
                    Console.WriteLine($"Leader changed from {this.primaryPerSlot[this.currentSlot - 1]} to {this.primaryPerSlot[this.currentSlot]}");
                    try
                    {
                        DoCleanup();
                    } catch (Exception e)
                    {
                        Console.WriteLine(e.StackTrace);
                        Console.WriteLine("--------------------------------------------");
                    }
                     
                }
                Console.WriteLine($"Preparation for slot {this.currentSlot} ended.");
            }
            
            Console.WriteLine("Ending preparation -----------------------");
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
                this.totalSlots == 0 ? 1 : this.currentSlot,
                request.ClientId,
                request.ClientSequenceNumber,
                -1,
                CommandType.Deposit,
                decimal.Parse(request.Value, CultureInfo.InvariantCulture)
            );

            while (this.isFrozen || this.isCleanning || this.totalSlots == 0)
            {
                Monitor.Wait(this);
            }

            Console.WriteLine($"Deposit ({request.Value}) from ({request.ClientId},{request.ClientSequenceNumber})");

            // If leader for the current slot (and command is from the current slot), start 2PC
            if (this.processId == this.primaryPerSlot[this.currentSlot] && command.Slot == this.currentSlot)
            {
                Do2PC(command);
            }

            // Wait for command to be committed (and applied) before sending response
            while (!this.committedCommands.ContainsKey((command.ClientId, command.ClientSequenceNumber)))
            {
                Monitor.Wait(this);
            }

            DepositReply reply = new DepositReply
            {
                Balance = this.balance.ToString(CultureInfo.InvariantCulture),
                Primary = this.primaryPerSlot[this.currentSlot] == this.processId,
            };
            
            Monitor.Exit(this);
            return reply; 
        }

        public WithdrawReply WithdrawMoney(WithdrawRequest request)
        {
            Monitor.Enter(this);

            ClientCommand command = new ClientCommand(
                this.totalSlots == 0 ? 1 : this.currentSlot,
                request.ClientId,
                request.ClientSequenceNumber,
                -1,
                CommandType.Withdraw,
                decimal.Parse(request.Value, CultureInfo.InvariantCulture)
            );

            while (this.isFrozen || this.isCleanning || this.totalSlots == 0)
            {
                Monitor.Wait(this); 
            }
            
            Console.WriteLine($"Withdraw ({request.Value}) from ({request.ClientId},{request.ClientSequenceNumber})");

            // If leader for the current slot, start 2PC
            if (this.processId == this.primaryPerSlot[this.currentSlot] && command.Slot == this.currentSlot)
            {
                Do2PC(command);
            }

            // Wait for command to be committed (and applied) before sending response
            while (!this.committedCommands.ContainsKey((command.ClientId, command.ClientSequenceNumber)))
            {
                Monitor.Wait(this);
            }

            bool success = this.committedCommands[(command.ClientId, command.ClientSequenceNumber)].Success;

            WithdrawReply reply = new WithdrawReply
            {
                Value = success ? request.Value : "0",
                Balance = this.balance.ToString(CultureInfo.InvariantCulture),
                Primary = this.primaryPerSlot[this.currentSlot] == this.processId,
            };
  
            Monitor.Exit(this);
            return reply;
        }

        public ReadReply ReadBalance(ReadRequest request)
        {
            Monitor.Enter(this);
            
            while (this.isFrozen || this.isCleanning || this.totalSlots == 0)
            {
                Monitor.Wait(this);              
            }

            Console.WriteLine($"Read from ({request.ClientId},{request.ClientSequenceNumber})");

            ReadReply reply = new ReadReply
            {
                Balance = balance.ToString(CultureInfo.InvariantCulture),
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
                // Should never happen since if the process is running then it could be the primary
                Console.WriteLine("No process is valid for leader election.");
                Console.WriteLine("No progress is going to be made in this slot.");
                if (slot > 1)
                    this.primaryPerSlot.Add(slot, this.primaryPerSlot[slot - 1]);
                return;
            }

            int electedPrimary = SendCompareAndSwapRequest(primary, slot);

            this.primaryPerSlot.Add(slot, electedPrimary);
            Monitor.PulseAll(this);

            Console.WriteLine($" >> Process {electedPrimary} is the primary for slot {slot}. << ");
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

            Task.WaitAny(tasks.ToArray());

            return compareAndSwapReplyValue;
        }

        /*
         * Two Phase Commit Service (Server) Implementation
         * Communication between BankServer and BankServer
         */

        public TentativeReply Tentative(TentativeRequest request)
        {
            Monitor.Enter(this);

            while (this.isFrozen)
            {
                Monitor.Wait(this);
            }
            
            Console.WriteLine($"({request.Command.Slot})    Tentative(id={request.ProcessId},cId={request.Command.ClientId},cSeq={request.Command.ClientSequenceNumber})");

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
            {
                ack = false;
            }

            (int, int) key = (request.Command.ClientId, request.Command.ClientSequenceNumber);
            ClientCommand newCommand = new ClientCommand(
                request.Command.Slot,
                request.Command.ClientId,
                request.Command.ClientSequenceNumber,
                request.Command.SequenceNumber,
                (CommandType)request.Command.Type,
                decimal.Parse(request.Command.Value, CultureInfo.InvariantCulture)
            );

            // Add to tentative dictionary
            // ack
            if (ack)
            {
                // Command is new
                if(!this.tentativeCommands.ContainsKey(key))
                {
                    this.tentativeCommands.Add(key, newCommand);
                } 
                // Command is not new but is tentative from a more recent slot
                else if(this.tentativeCommands[key].Slot < request.Command.Slot)
                {
                    this.tentativeCommands.Remove(key);
                    this.tentativeCommands.Add(key, newCommand);
                }
                Monitor.PulseAll(this);
            }

            Monitor.Exit(this);
            return new TentativeReply
            {
                Acknowledge = ack,
            }; 
        }

        public CommitReply Commit(CommitRequest request)
        {
            Monitor.Enter(this);

            while (this.isFrozen)
            {
                Monitor.Wait(this);
            }

            (int, int) key = (request.Command.ClientId, request.Command.ClientSequenceNumber);

            // Wait for command to be tentative
            ClientCommand tentativeCommand;
            while (!this.tentativeCommands.TryGetValue(key, out tentativeCommand))
            {
                Monitor.Wait(this);
            }

            Console.WriteLine($"({request.Command.Slot})        Commit(id={request.ProcessId},seq={request.Command.SequenceNumber},cId={request.Command.ClientId},cSeq={request.Command.ClientSequenceNumber})");

            // Wait for previous commits, assumes no holes in history
            while (this.committedCommands.Count < request.Command.SequenceNumber-1)
            {
                Monitor.Wait(this);
            }

            // Commit may be repeated and should be accepted but not applied
            if (this.committedCommands.ContainsKey(key))
            {
                this.committedCommands.Remove(key);
            }
            else
            {
                // Apply command
                decimal value = decimal.Parse(request.Command.Value, CultureInfo.InvariantCulture);
                switch (request.Command.Type)
                {
                    case (Type.Deposit):
                        
                        this.balance += value;
                        break;
                        
                    case (Type.Withdraw):
                        if (value <= this.balance)
                        {
                            this.balance -= value;
                            tentativeCommand.Success = true;
                        }
                        else
                        {
                            tentativeCommand.Success = false;
                        }
                        break;
                }
            }

            this.committedCommands.Add(key, tentativeCommand);
            this.currentSequenceNumber = tentativeCommand.SequenceNumber;

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
         */

        public void Do2PC(ClientCommand command)
        {
            Console.WriteLine($"({command.Slot}) 2PC(cId={command.ClientId},cSeq={command.ClientSequenceNumber})");

            // keep sequence number local to ensure tentative and commit are called with the same sequence number
            int sequenceNumber = this.currentSequenceNumber;
            sequenceNumber++;

            Monitor.Exit(this);
            bool tentativeSuccess = SendTentativeRequest(sequenceNumber, command);
            Monitor.Enter(this);
            
            // If primary changes after sending tentatives, abort
            if (tentativeSuccess && this.primaryPerSlot[this.currentSlot] == this.processId)
            {
                // Process may get frozen in the middle of 2PC
                while (this.isFrozen)
                {
                    Monitor.Wait(this);
                }
                
                Monitor.Exit(this);
                SendCommitRequest(sequenceNumber, command);
                Monitor.Enter(this);
                
                this.currentSequenceNumber = sequenceNumber;
                Console.WriteLine($"({command.Slot}) OK 2PC(cId={command.ClientId},cSeq={command.ClientSequenceNumber})");
            } 
            else
            {
                Console.WriteLine($"({command.Slot}) NOK 2PC(cId={command.ClientId},cSeq={command.ClientSequenceNumber})");
            }
        }
        
        public bool SendTentativeRequest(int sequenceNumber, ClientCommand command)
        {
            Console.WriteLine($"({command.Slot}) Sending tentatives (cId={command.ClientId},cSeq={command.ClientSequenceNumber})");

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
                    Value = command.Value.ToString(CultureInfo.InvariantCulture),
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
            Console.WriteLine($"({command.Slot}) Sending commits (seq={sequenceNumber},cId={command.ClientId},cSeq={command.ClientSequenceNumber})");

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
                    Value = command.Value.ToString(CultureInfo.InvariantCulture),
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
                    }
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
         */

        public ListPendingRequestsReply ListPendingRequests(ListPendingRequestsRequest request)
        {
            Monitor.Enter(this);

            while (this.isFrozen)
            {
                Monitor.Wait(this);
            }

            // Transform every tentativeCommand into a Command from grpc
            List<Command> tentativeCommands = new List<Command>();
            foreach (var tentativeCommand in this.tentativeCommands)
            {
                if (tentativeCommand.Value.SequenceNumber > request.LastKnownSequenceNumber)
                {
                    tentativeCommands.Add(new Command
                    {
                        Slot = tentativeCommand.Value.Slot,
                        ClientId = tentativeCommand.Value.ClientId,
                        ClientSequenceNumber = tentativeCommand.Value.ClientSequenceNumber,
                        SequenceNumber = tentativeCommand.Value.SequenceNumber,
                        Type = (Type)tentativeCommand.Value.Type,
                        Value = tentativeCommand.Value.Value.ToString(CultureInfo.InvariantCulture),
                    });
                }
            }
            
            Monitor.Exit(this);
            return new ListPendingRequestsReply
            {
                Commands = { tentativeCommands },
            };
        }

        /*
         * Cleanup Service (Client) Implementation
         * Communication between BankServer and BankServer
         */
        
        public void DoCleanup()
        {
            this.isCleanning = true;
            
            Console.WriteLine("Starting cleanup");

            // every tentative command from replicas
            Monitor.Exit(this);
            List<ClientCommand> clientCommands = SendListPendingRequestsRequest();
            Monitor.Enter(this);

            foreach (ClientCommand command in clientCommands)
            {    
                // command slot may be outdated, needs update
                command.Slot = this.currentSlot;
                Do2PC(command);
            }

            this.isCleanning = false;

            Console.WriteLine("Finished cleanup");
            Monitor.PulseAll(this);
        }

        public List<ClientCommand> SendListPendingRequestsRequest()
        {
            Console.WriteLine("Sending list pending requests");

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
                        commands.Add(new ClientCommand
                        (
                            command.Slot,
                            command.ClientId,
                            command.ClientSequenceNumber,
                            command.SequenceNumber,
                            (CommandType)command.Type,
                            decimal.Parse(command.Value, CultureInfo.InvariantCulture)
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

            Console.WriteLine($"Received {commands.Count} pending request");

            return commands;
        }
    }
}
