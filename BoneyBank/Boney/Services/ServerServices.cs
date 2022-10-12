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

        // Variables that depend on the slot
        private ConcurrentDictionary<int, SlotData> slots = new ConcurrentDictionary<int, SlotData>();

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
        }

        /*
         * Prepare Slot
         * TODO: Description
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

            Console.WriteLine($"Preparing slot {this.currentSlot}...");

            SlotData slot = new SlotData(this.currentSlot);
            slots.TryAdd(this.currentSlot, slot);

            // TODO é preciso fazer alguma coisa no Boney quando começa um slot ?

            Console.WriteLine($"Preparation for slot {this.currentSlot} ended.");
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
            while (!this.slots.ContainsKey(request.Slot))
            {
                // wait for slot to be created
                Console.WriteLine($"Waiting (slot creation)... - {Thread.CurrentThread.ManagedThreadId}");
                Monitor.Wait(this);
                Console.WriteLine($"Waking up... - {Thread.CurrentThread.ManagedThreadId}");
            }

            while (this.isFrozen)
            {
                // wait for slot to be unfrozen
                Console.WriteLine($"Waiting (frozen)... - {Thread.CurrentThread.ManagedThreadId}");
                Monitor.Wait(this);
                Console.WriteLine($"Waking up... - {Thread.CurrentThread.ManagedThreadId}");
            }

            SlotData slot;
            PromiseReply reply;


            slot = this.slots[request.Slot];

            if (slot.ReadTimestamp < request.LeaderId)
            {
                slot.ReadTimestamp = request.LeaderId;
            }

            reply = new PromiseReply
            {
                Slot = request.Slot,
                ReadTimestamp = slot.ReadTimestamp,
                Value = slot.CompareAndSwapValue,
            };

            Console.WriteLine($"({request.Slot})--> Prepare({request.LeaderId})");
            Console.WriteLine($"({request.Slot})    <-- Promise({slot.ReadTimestamp},{slot.CompareAndSwapValue})");

            Monitor.Exit(this);
            return reply;
        }

        public AcceptedReply AcceptPaxos(AcceptRequest request)
        {
            Monitor.Enter(this);
            while (!this.slots.ContainsKey(request.Slot))
            {
                // wait for slot to be created
                Console.WriteLine($"Waiting (slot creation)... - {Thread.CurrentThread.ManagedThreadId}");
                Monitor.Wait(this);
                Console.WriteLine($"Waking up... - {Thread.CurrentThread.ManagedThreadId}");
            }

            while (this.isFrozen)
            {
                // wait for slot to be unfrozen
                Console.WriteLine($"Waiting (frozen)... - {Thread.CurrentThread.ManagedThreadId}");
                Monitor.Wait(this);
                Console.WriteLine($"Waking up... - {Thread.CurrentThread.ManagedThreadId}");
            }

            SlotData slot = this.slots[request.Slot];

            Console.WriteLine($"({request.Slot})--> Accept({request.LeaderId}, {request.Value})");

            if (slot.WriteTimestamp < request.LeaderId)
            {
                slot.WriteTimestamp = request.LeaderId;
                slot.CompareAndSwapValue = request.Value;

                // Acceptors send the information to Learners
                SendDecideRequest(slot.Slot, slot.WriteTimestamp, request.Value);
            }

            Console.WriteLine($"({request.Slot})    <-- Accepted({slot.WriteTimestamp},{slot.CompareAndSwapValue})");

            AcceptedReply reply = new AcceptedReply
            {
                Slot = request.Slot,
                WriteTimestamp = slot.WriteTimestamp,
                Value = slot.CompareAndSwapValue,
            };

            Monitor.Exit(this);
            return reply;
        }

        public DecideReply DecidePaxos(DecideRequest request)
        {
            Monitor.Enter(this);
            while (!this.slots.ContainsKey(request.Slot))
            {
                // wait for slot to be created
                Console.WriteLine($"Waiting (slot creation)... - {Thread.CurrentThread.ManagedThreadId}");
                Monitor.Wait(this);
                Console.WriteLine($"Waking up... - {Thread.CurrentThread.ManagedThreadId}");
            }

            while (this.isFrozen)
            {
                // wait for slot to be unfrozen
                Console.WriteLine($"Waiting (frozen)... - {Thread.CurrentThread.ManagedThreadId}");
                Monitor.Wait(this);
                Console.WriteLine($"Waking up... - {Thread.CurrentThread.ManagedThreadId}");//
            }

            SlotData slot = this.slots[request.Slot];

            Console.WriteLine($"({request.Slot})--> Decide({request.WriteTimestamp},{request.Value})");

            slot.DecidedReceived.Add((request.WriteTimestamp, request.Value));

            int majority = this.boneyHosts.Count / 2 + 1;

            Dictionary<(int, int), int> received = new Dictionary<(int, int), int>();
            foreach (var entry in slot.DecidedReceived)
            {
                if (received.ContainsKey(entry))
                {
                    received[entry]++;
                }
                else
                {
                    received.Add(entry, 1);
                }
            }
            foreach (KeyValuePair<(int, int), int> kvp in received)
            {
                // Learners have received a majority of accepted() with the same value
                // Therefore the paxos has reached a consensus
                if (kvp.Value >= majority)
                {
                    slot.CurrentValue = kvp.Key.Item2;
                    slot.IsPaxosRunning = false;
                    Monitor.PulseAll(this);
                }
            }
            // USEFULL TO PRINT DICTIONARIES
            //received.Select(i => $"{i.Key}: {i.Value}").ToList().ForEach(Console.WriteLine);

            Console.WriteLine($"({request.Slot})    <-- Decided()");
            Monitor.Exit(this);
            return new DecideReply
            {
                // empty ?
            };
        }

        /*
        * Paxos Service (Client) Implementation
        * Communication between Boney and Boney
        */

        public List<PromiseReply> SendPrepareRequest(int slot)
        {

            //Console.WriteLine($"Sending prepares with leader id {this.processId}");

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
            //Console.WriteLine($"Sending accepts with leader id {this.processId}");

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

            Console.WriteLine($"Sending decides with value {value}");

            DecideRequest decideRequest = new DecideRequest
            {
                Slot = slot,
                WriteTimestamp = writeTimestamp,
                Value = value
            };

            // Send request to all boney processes
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
            //Console.WriteLine($"Waiting for paxos to end. - {Thread.CurrentThread.ManagedThreadId}");
            while (slot.IsPaxosRunning)
            {
                // wait for paxos to end
                Console.WriteLine($"Waiting (paxos to end)... - {Thread.CurrentThread.ManagedThreadId}");
                Monitor.Wait(this);
                Console.WriteLine($"Waking up... - {Thread.CurrentThread.ManagedThreadId}");
            }

            Console.WriteLine($"Paxos ended with value {slot.CurrentValue} - {Thread.CurrentThread.ManagedThreadId}");
            CompareAndSwapReply reply = new CompareAndSwapReply
            {
                Slot = request.Slot,
                Outvalue = slot.CurrentValue,
            };

            Monitor.Exit(this);
            return reply;
        }

        public CompareAndSwapReply CompareAndSwap(CompareAndSwapRequest request)
        {
            Monitor.Enter(this);
            while (!this.slots.ContainsKey(request.Slot))
            {
                // wait for slot to be created
                Console.WriteLine($"Waiting (slot creation)... - {Thread.CurrentThread.ManagedThreadId}");
                Monitor.Wait(this);
                Console.WriteLine($"Waking up... - {Thread.CurrentThread.ManagedThreadId}");
            }

            while (this.isFrozen)
            {
                // wait for process to be unfrozen
                Console.WriteLine($"Waiting (frozen)... - {Thread.CurrentThread.ManagedThreadId}");
                Monitor.Wait(this);
                Console.WriteLine($"Waking up... - {Thread.CurrentThread.ManagedThreadId}");
            }


            SlotData slot;
            bool needsToWait;

            slot = this.slots[request.Slot];
            Console.WriteLine($"Compare and swap request with value {request.Invalue} in slot {request.Slot}");

            needsToWait = slot.CompareAndSwapValue != -1 || slot.CurrentValue != -1;
            // needs better names
            // CompareAndSwapValue -> valor que foi trocado pelo banco
            // CurrentValue -> Valor que foi decido pelo paxos
            if (!needsToWait)
            {
                slot.CompareAndSwapValue = request.Invalue;
                slot.IsPaxosRunning = true;
            }

            if (needsToWait)
            {
                return WaitForPaxosToEnd(slot, request);
            }

            // Do paxos
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
                //if (response.ReadTimestamp > slot.ReadTimestamp)
                if (response.ReadTimestamp > this.processId)
                {
                    //Console.WriteLine($"{this.processId} KILL SELF");
                    return WaitForPaxosToEnd(slot, request);
                }
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
                valueToPropose = slot.CompareAndSwapValue;

            Monitor.Exit(this);
            // Send accept to all acceptors which will send decide to learners
            List<AcceptedReply> acceptResponses = SendAcceptRequest(request.Slot, valueToPropose);
            Monitor.Enter(this);
            // Wait for learners to decide
            return WaitForPaxosToEnd(slot, request);
        }
    }
}
