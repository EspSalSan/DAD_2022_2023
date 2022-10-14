using Boney.Domain;
using Google.Protobuf.WellKnownTypes;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Boney.Services
{
    public class ServerService
    {
        // Config file variables
        private int processId;
        private readonly List<bool> processFrozenPerSlot;
        private readonly Dictionary<int, Paxos.PaxosClient> boneyHosts;
        private readonly List<Dictionary<int, bool>> processesSuspectedPerSlot;

        // Changing variables
        private bool isFrozen;
        private int currentSlot;
        private readonly ConcurrentDictionary<int, SlotData> slots;

        public ServerService(
            int processId,
            List<bool> processFrozenPerSlot,
            List<Dictionary<int, bool>> processesSuspectedPerSlot,
            Dictionary<int, Paxos.PaxosClient> boneyHosts
            )
        {
            this.processId = processId;
            this.boneyHosts = boneyHosts;
            this.processFrozenPerSlot = processFrozenPerSlot;
            this.processesSuspectedPerSlot = processesSuspectedPerSlot;

            this.currentSlot = 0;
            this.isFrozen = false;

            this.slots = new ConcurrentDictionary<int, SlotData>();
            // Initialize slots
            for (int i = 1; i <= processFrozenPerSlot.Count; i++)
                this.slots.TryAdd(i, new SlotData(i));
        }

        /*
         * At the start of every slot this function is called to "prepare the slot".
         * Updates process state (frozen or not).
         * Creates new entry for the slot in the slots dictionary.
         */
        public void PrepareSlot()
        {
            Monitor.Enter(this);
            if (this.currentSlot >= processFrozenPerSlot.Count)
            {
                Console.WriteLine("Slot duration ended but no more slots to process.");
                return;
            }

            // Switch process state
            this.isFrozen = this.processFrozenPerSlot[currentSlot];
            if (this.currentSlot > 0)
                this.slots[this.currentSlot].IsPaxosRunning = false;
            Monitor.PulseAll(this);
            Console.WriteLine($"Process is now {(this.isFrozen ? "frozen" : "normal")} for slot {currentSlot+1}");

            this.currentSlot += 1;

            // Every slot increase processId to allow progress when the system configuration changes
            this.processId += this.boneyHosts.Count;

            Monitor.Exit(this);
        }

        /*
        * Paxos Service (Server) Implementation
        * Communication between Boney and Boney
        */

        public PromiseReply PreparePaxos(PrepareRequest request)
        {
            Monitor.Enter(this);
            while (this.isFrozen)
            {
                Monitor.Wait(this);
            }

            SlotData slot = this.slots[request.Slot];
  
            if (slot.ReadTimestamp < request.LeaderId)
                slot.ReadTimestamp = request.LeaderId;

            PromiseReply reply = new PromiseReply
            {
                Slot = request.Slot,
                ReadTimestamp = slot.ReadTimestamp,
                Value = slot.WrittenValue,
            };

            Console.WriteLine($"({request.Slot})    Received Prepare({request.LeaderId})");
            Console.WriteLine($"({request.Slot})        Answered Promise({slot.ReadTimestamp},{slot.WrittenValue})");

            Monitor.Exit(this);
            return reply;
        }

        public AcceptedReply AcceptPaxos(AcceptRequest request)
        {
            Monitor.Enter(this);
            while (this.isFrozen)
            {
                Monitor.Wait(this);
            }

            SlotData slot = this.slots[request.Slot];

            Console.WriteLine($"({request.Slot})    Recevied Accept({request.LeaderId}, {request.Value})");

            if (slot.ReadTimestamp == request.LeaderId)
            {
                slot.WriteTimestamp = request.LeaderId;
                slot.WrittenValue = request.Value;

                // Acceptors send the information to Learners
                SendDecideRequest(slot.Slot, slot.WriteTimestamp, request.Value);
            }

            Console.WriteLine($"({request.Slot})        Answered Accepted({slot.WriteTimestamp},{slot.WrittenValue})");

            AcceptedReply reply = new AcceptedReply
            {
                Slot = request.Slot,
                WriteTimestamp = slot.WriteTimestamp,
                Value = slot.WrittenValue,
            };

            Monitor.Exit(this);
            return reply;
        }

        public DecideReply DecidePaxos(DecideRequest request)
        {
            Monitor.Enter(this);
            while (this.isFrozen)
            {
                Monitor.Wait(this);
            }

            SlotData slot = this.slots[request.Slot];

            Console.WriteLine($"({request.Slot})    Recevied Decide({request.WriteTimestamp},{request.Value})");

            // Learners keep track of all decided values to check for a majority
            slot.DecidedReceived.Add((request.WriteTimestamp, request.Value));

            int majority = this.boneyHosts.Count / 2 + 1;

            // Create a dictionary to count the number of times a request appears
            Dictionary<(int, int), int> receivedRequests = new Dictionary<(int, int), int>();
            foreach (var entry in slot.DecidedReceived)
            {
                if (receivedRequests.ContainsKey(entry))
                    receivedRequests[entry]++;
                else
                    receivedRequests.Add(entry, 1);
            }
            
            // If a request appears more times than the majority value, it's the decided value
            foreach (KeyValuePair<(int, int), int> requestFrequency in receivedRequests)
            {
                if (requestFrequency.Value >= majority)
                {
                    slot.DecidedValue = requestFrequency.Key.Item2;
                    slot.IsPaxosRunning = false;
                    Monitor.PulseAll(this);
                }
            }

            Console.WriteLine($"({request.Slot})        Answered Decided()");
            Monitor.Exit(this);
            return new DecideReply
            {
            };
        }

        /*
        * Paxos Service (Client) Implementation
        * Communication between Boney and Boney
        */

        public List<PromiseReply> SendPrepareRequest(int slot, int leaderId)
        {
            PrepareRequest prepareRequest = new PrepareRequest
            {
                Slot = slot,
                LeaderId = leaderId
            };

            Console.WriteLine($"({slot}) Sending Prepare({leaderId})");

            List<PromiseReply> promiseResponses = new List<PromiseReply>();

            List<Task> tasks = new List<Task>();
            foreach (var host in this.boneyHosts)
            {
                Task t = Task.Run(() =>
                {
                    try
                    {
                        PromiseReply promiseReply = host.Value.Prepare(prepareRequest);
                        promiseResponses.Add(promiseReply);
                    }
                    catch (Grpc.Core.RpcException e)
                    {
                        Console.WriteLine(e.Status);
                    }
                    return Task.CompletedTask;
                });
                tasks.Add(t);
            }

            for (int i = 0; i < this.boneyHosts.Count / 2 + 1; i++)
                tasks.RemoveAt(Task.WaitAny(tasks.ToArray()));

            return promiseResponses;
        }

        public List<AcceptedReply> SendAcceptRequest(int slot, int leaderId, int value)
        {
            AcceptRequest acceptRequest = new AcceptRequest
            {
                Slot = slot,
                LeaderId = leaderId,
                Value = value,
            };
            
            Console.WriteLine($"({slot}) Sending Accept({leaderId},{value})");

            List<AcceptedReply> acceptResponses = new List<AcceptedReply>();

            List<Task> tasks = new List<Task>();
            foreach (var host in this.boneyHosts)
            {
                Task t = Task.Run(() =>
                {
                    try
                    {
                        AcceptedReply acceptedReply = host.Value.Accept(acceptRequest);
                        acceptResponses.Add(acceptedReply);
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

            return acceptResponses;
        }

        public void SendDecideRequest(int slot, int writeTimestamp, int value)
        {

            DecideRequest decideRequest = new DecideRequest
            {
                Slot = slot,
                WriteTimestamp = writeTimestamp,
                Value = value
            };

            Console.WriteLine($"({slot}) Sending Decide({writeTimestamp},{value})");

            foreach (var host in this.boneyHosts)
            {
                Task t = Task.Run(() =>
                {
                    try
                    {
                        DecideReply decideReply = host.Value.Decide(decideRequest);
                    }
                    catch (Grpc.Core.RpcException e)
                    {
                        Console.WriteLine(e.Status);
                    }
                    return Task.CompletedTask;
                });
            }

            // Don't need to wait for majority
        }

        /*
         * Compare And Swap Service (Server) Implementation
         * Communication between Bank and Boney
         */

        public bool WaitForPaxos(SlotData slot, CompareAndSwapRequest request)
        {
            bool success = true;
            while (slot.IsPaxosRunning)
            {
                Monitor.Wait(this);

                // Slot ended without reaching consensus
                // Do paxos again with another configuration
                if (this.currentSlot > slot.Slot && slot.DecidedValue == -1)
                {
                    Console.WriteLine($"Slot {slot.Slot} ended without consensus, starting a new paxos instance in slot {this.currentSlot}.");
                    success = false;
                    break;
                }
            }
            return success;
        }

        public bool DoPaxos(CompareAndSwapRequest request)
        {
            //Monitor.Enter(this);
            
            SlotData slot = this.slots[request.Slot];

            // If paxos isn't running and a value hasn't been decided, start paxos
            if (!slot.IsPaxosRunning && slot.DecidedValue == -1)
            {
                //if(slot.WrittenValue == -1)
                //    slot.WrittenValue = request.Invalue;
                
                slot.IsPaxosRunning = true;
            }
            else
            {
                return WaitForPaxos(slot, request);
            }

            Console.WriteLine($"Starting Paxos instance in slot {this.currentSlot} for slot {request.Slot}");

            // Select new leader
            Dictionary<int, bool> processesSuspected = this.processesSuspectedPerSlot[currentSlot - 1];
            int leader = int.MaxValue;
            foreach (KeyValuePair<int, bool> process in processesSuspected)
            {
                // Boney process that is not suspected and has the lowest id
                if (!process.Value && process.Key < leader && this.boneyHosts.ContainsKey(process.Key))
                    leader = process.Key;
            }

            if (leader == int.MaxValue)
            {
                // this should never happen, if process is running then he can be the leader  
            }
            
            Console.WriteLine($"Paxos leader is {leader} in slot {this.currentSlot} for slot {request.Slot}");

            // Save processId for current paxos instance
            // Otherwise it might change in the middle of paxos if a new slot begins
            int leaderCurrentId = this.processId;
            
            // 'leader' comes from config, doesnt account for increase in processId
            if (this.processId%3 != leader)
            {
                return WaitForPaxos(slot, request);
            }

            Monitor.Exit(this);
            // Send prepare to all acceptors
            List<PromiseReply> promiseResponses = SendPrepareRequest(request.Slot, leaderCurrentId);
            
            Monitor.Enter(this);
            // Stop being leader if there is a more recent one
            foreach (var response in promiseResponses)
            {
                if (response.ReadTimestamp > this.processId)
                    return WaitForPaxos(slot, request);
            }

            // Get values from promises
            int mostRecent = -1;
            int valueToPropose = -1;
            foreach (var response in promiseResponses)
            {
                if (response.ReadTimestamp > mostRecent)
                {
                    mostRecent = response.ReadTimestamp;
                    valueToPropose = response.Value;
                }
            }

            // If acceptors have no value, send own value
            if (valueToPropose == -1)
                valueToPropose = request.Invalue;
                //valueToPropose = slot.WrittenValue;

            Monitor.Exit(this);
            // Send accept to all acceptors which will send decide to all learners
            SendAcceptRequest(request.Slot, leaderCurrentId, valueToPropose);

            Monitor.Enter(this);
            // Wait for learners to decide
            return WaitForPaxos(slot, request);
        }

        public CompareAndSwapReply CompareAndSwap(CompareAndSwapRequest request)
        {
            Monitor.Enter(this);
            while (this.isFrozen)
            {
                Monitor.Wait(this);
            }

            SlotData slot = this.slots[request.Slot];
        
            Console.WriteLine($"Compare and swap request with value {request.Invalue} in slot {request.Slot}");

            //Monitor.Exit(this);

            while (!DoPaxos(request))
            {
            }
            
            Monitor.Exit(this);

            Console.WriteLine($"Compare and swap replied with value {slot.DecidedValue} for slot {request.Slot}");
            CompareAndSwapReply reply = new CompareAndSwapReply
            {
                Slot = request.Slot,
                Outvalue = slot.DecidedValue,
            };

            return reply;
        }
    }
}
