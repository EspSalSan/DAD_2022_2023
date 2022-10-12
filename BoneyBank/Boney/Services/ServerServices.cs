using Boney.Domain;
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
        private readonly int processId;
        private readonly List<Dictionary<int, bool>> processesSuspectedPerSlot;
        private readonly List<bool> processFrozenPerSlot;
        private readonly Dictionary<int, Paxos.PaxosClient> boneyHosts;

        // Changing variables
        private int currentSlot;
        private bool isFrozen;
        private readonly ConcurrentDictionary<int, SlotData> slots;

        public ServerService(
            int processId,
            List<bool> processFrozenPerSlot,
            List<Dictionary<int, bool>> processesSuspectedPerSlot,
            Dictionary<int, Paxos.PaxosClient> boneyHosts
            )
        {
            this.processId = processId;
            this.processesSuspectedPerSlot = processesSuspectedPerSlot;
            this.processFrozenPerSlot = processFrozenPerSlot;
            this.boneyHosts = boneyHosts;

            this.currentSlot = 0;
            this.isFrozen = false;

            this.slots = new ConcurrentDictionary<int, SlotData>();
            // Initialize slots
            for (int i = 1; i <= processFrozenPerSlot.Count; i++)
            {
                
                bool result = this.slots.TryAdd(i, new SlotData(i));
                Console.WriteLine($"Created slot {i} with result: {result}");
            }
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
            Console.WriteLine($"Process is now {(this.isFrozen ? "frozen" : "normal")}");

            this.currentSlot += 1;

            Monitor.PulseAll(this);
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
                Console.WriteLine($"Waiting (frozen)... - {Thread.CurrentThread.ManagedThreadId}");
                Monitor.Wait(this);
                Console.WriteLine($"Waking up... - {Thread.CurrentThread.ManagedThreadId}");
            }

            SlotData slot = this.slots[request.Slot];
  
            if (slot.ReadTimestamp < request.LeaderId)
            {
                slot.ReadTimestamp = request.LeaderId;
            }

            PromiseReply reply = new PromiseReply
            {
                Slot = request.Slot,
                ReadTimestamp = slot.ReadTimestamp,
                Value = slot.WrittenValue,
            };

            Console.WriteLine($"({request.Slot})--> Prepare({request.LeaderId})");
            Console.WriteLine($"({request.Slot})    <-- Promise({slot.ReadTimestamp},{slot.WrittenValue})");

            Monitor.Exit(this);
            return reply;
        }

        public AcceptedReply AcceptPaxos(AcceptRequest request)
        {
            Monitor.Enter(this);
            while (this.isFrozen)
            {
                // wait for slot to be unfrozen
                Console.WriteLine($"Waiting (frozen)... - {Thread.CurrentThread.ManagedThreadId}");
                Monitor.Wait(this);
                Console.WriteLine($"Waking up... - {Thread.CurrentThread.ManagedThreadId}");
            }

            SlotData slot = this.slots[request.Slot];

            Console.WriteLine($"({request.Slot})--> Accept({request.LeaderId}, {request.Value})");

            if (slot.ReadTimestamp == request.LeaderId)
            {
                slot.WriteTimestamp = request.LeaderId;
                slot.WrittenValue = request.Value;

                // Acceptors send the information to Learners
                SendDecideRequest(slot.Slot, slot.WriteTimestamp, request.Value);
            }

            Console.WriteLine($"({request.Slot})    <-- Accepted({slot.WriteTimestamp},{slot.WrittenValue})");

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
                // wait for slot to be unfrozen
                Console.WriteLine($"Waiting (frozen)... - {Thread.CurrentThread.ManagedThreadId}");
                Monitor.Wait(this);
                Console.WriteLine($"Waking up... - {Thread.CurrentThread.ManagedThreadId}");//
            }

            SlotData slot = this.slots[request.Slot];

            Console.WriteLine($"({request.Slot})--> Decide({request.WriteTimestamp},{request.Value})");

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
            // If a request appears more than the majority, it is the decided value
            foreach (KeyValuePair<(int, int), int> requestFrequency in receivedRequests)
            {
                if (requestFrequency.Value >= majority)
                {
                    slot.DecidedValue = requestFrequency.Key.Item2;
                    slot.IsPaxosRunning = false;
                    Monitor.PulseAll(this);
                }
            }

            Console.WriteLine($"({request.Slot})    <-- Decided()");
            Monitor.Exit(this);
            return new DecideReply
            {
            };
        }

        /*
        * Paxos Service (Client) Implementation
        * Communication between Boney and Boney
        */

        public List<PromiseReply> SendPrepareRequest(int slot)
        {
            PrepareRequest prepareRequest = new PrepareRequest
            {
                Slot = slot,
                LeaderId = this.processId
            };

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

        public List<AcceptedReply> SendAcceptRequest(int slot, int value)
        {
            AcceptRequest acceptRequest = new AcceptRequest
            {
                Slot = slot,
                LeaderId = this.processId,
                Value = value,
            };

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

        public CompareAndSwapReply WaitForPaxosToEnd(SlotData slot, CompareAndSwapRequest request)
        {
            while (slot.IsPaxosRunning)
            {
                // wait for paxos to end
                Console.WriteLine($"Waiting (paxos to end)... - {Thread.CurrentThread.ManagedThreadId}");
                Monitor.Wait(this);
                Console.WriteLine($"Waking up... - {Thread.CurrentThread.ManagedThreadId}");
            }

            Console.WriteLine($"Paxos ended with value {slot.DecidedValue} - {Thread.CurrentThread.ManagedThreadId}");
            CompareAndSwapReply reply = new CompareAndSwapReply
            {
                Slot = request.Slot,
                Outvalue = slot.DecidedValue,
            };

            Monitor.Exit(this);
            return reply;
        }

        public CompareAndSwapReply CompareAndSwap(CompareAndSwapRequest request)
        {
            Monitor.Enter(this);
            while (this.isFrozen)
            {
                // wait for process to be unfrozen
                Console.WriteLine($"Waiting (frozen)... - {Thread.CurrentThread.ManagedThreadId}");
                Monitor.Wait(this);
                Console.WriteLine($"Waking up... - {Thread.CurrentThread.ManagedThreadId}");
            }

            SlotData slot = this.slots[request.Slot];
        
            Console.WriteLine($"Compare and swap request with value {request.Invalue} in slot {request.Slot}");

            if (slot.WrittenValue == -1 && slot.DecidedValue == -1)
            {
                slot.WrittenValue = request.Invalue;
                slot.IsPaxosRunning = true;
            }
            else
            {
                return WaitForPaxosToEnd(slot, request);
            }

            Console.WriteLine("Starting Paxos...");

            // Compute paxos leader (lowest id with NS)
            // Select new leader
            Dictionary<int, bool> processesSuspected = this.processesSuspectedPerSlot[currentSlot - 1];
            int leader = int.MaxValue;
            foreach (KeyValuePair<int, bool> process in processesSuspected)
            {
                // Process that is not suspected and has the lowest id
                if (!process.Value && process.Key < leader && this.boneyHosts.ContainsKey(process.Key))
                    leader = process.Key;
            }

            if (leader == int.MaxValue)
            {
                // this should never happen, if process is running then he can be the leader  
            }

            Console.WriteLine($"Paxos Leader is {leader}");

            if (this.processId != leader)
            {
                return WaitForPaxosToEnd(slot, request);
            }

            Monitor.Exit(this);

            // Send prepare to all acceptors
            List<PromiseReply> promiseResponses = SendPrepareRequest(request.Slot);
            Monitor.Enter(this);
            // Stop being leader if there is a more recent one
            foreach (var response in promiseResponses)
            {
                if (response.ReadTimestamp > this.processId)
                    return WaitForPaxosToEnd(slot, request);
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
                valueToPropose = slot.WrittenValue;

            Monitor.Exit(this);
            // Send accept to all acceptors which will send decide to learners
            SendAcceptRequest(request.Slot, valueToPropose);
            
            Monitor.Enter(this);
            // Wait for learners to decide
            return WaitForPaxosToEnd(slot, request);
        }
    }
}
