using Boney.Domain;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

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
         * TODO: Description
         */
        public void PrepareSlot()
        {
            lock (this)
            {
                if (this.currentSlot >= processFrozenPerSlot.Count)
                {
                    Console.WriteLine("Slot duration ended but no more slots to process.");
                    return;
                }

                this.currentSlot += 1;

                Console.WriteLine($"Preparing slot {this.currentSlot}.");

                SlotData slot = new SlotData(this.currentSlot);
                slots.TryAdd(this.currentSlot, slot);

                // TODO é preciso fazer alguma coisa no Boney quando começa um slot ?

                Console.WriteLine($"Preparation for slot {this.currentSlot} ended.");
            }
        }

        /*
        * Paxos Service (Client) Implementation
        * Communication between Boney and Boney
        */

        public List<PromiseReply> SendPrepareRequest()
        {
            PrepareRequest prepareRequest = new PrepareRequest
            {
                Slot = this.currentSlot,
                LeaderId = this.processId
            };

            List<PromiseReply> promiseResponses = new List<PromiseReply>();

            // TODO: Use tasks and wait majority

            foreach (var host in this.boneyHosts)
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
            }

            Console.WriteLine($"Sent {promiseResponses.Count} prepare");

            // Wait for promise(readTS, value) from majority

            return promiseResponses;
        }

        public List<AcceptedReply> SendAcceptRequest(int value)
        {
            Console.WriteLine("Sending accept");

            AcceptRequest acceptRequest = new AcceptRequest
            {
                Slot = this.currentSlot,
                LeaderId = this.processId,
                Value = value,
            };

            List<AcceptedReply> acceptResponses = new List<AcceptedReply>();

            foreach (var host in this.boneyHosts)
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
            }

            // Wait for accepted(processId, value) from majority
            Console.WriteLine($"Sent {acceptResponses.Count} accept");

            return acceptResponses;
        }

        public void SendDecideRequest(int writeTimestamp, int value)
        {
            DecideRequest decideRequest = new DecideRequest
            {
                Slot = this.currentSlot,
                WriteTimestamp = writeTimestamp,
                Value = value
            };

            foreach (var host in this.boneyHosts)
            {
                try
                {
                    DecideReply decideReply = host.Value.Decide(decideRequest);
                    Console.WriteLine("Ended decide");
                }
                catch (Grpc.Core.RpcException e)
                {
                    Console.WriteLine(e.Status);
                }
            }
        }

        /*
        * Paxos Service (Server) Implementation
        * Communication between Boney and Boney
        */

        public PromiseReply PreparePaxos(PrepareRequest request)
        {
            while (!this.slots.ContainsKey(request.Slot))
            {
                // wait for slot to be created
            }

            SlotData slot = this.slots[request.Slot];

            Console.WriteLine($"Prepare request from {request.LeaderId} in slot {request.Slot}, returning {slot.CompareAndSwapValue}");

            if (slot.ReadTimestamp < request.LeaderId)
            {
                slot.ReadTimestamp = request.LeaderId;
            }
            return new PromiseReply
            {
                Slot = request.Slot,
                ReadTimestamp = slot.ReadTimestamp,
                Value = slot.CompareAndSwapValue,
            };
        }

        public AcceptedReply AcceptPaxos(AcceptRequest request)
        {
            while (!this.slots.ContainsKey(request.Slot))
            {
                // wait for slot to be created
            }

            SlotData slot = this.slots[request.Slot];

            Console.WriteLine($"Accept request from {request.LeaderId} in slot {request.Slot}");

            if (slot.WriteTimestamp < request.LeaderId)
            {
                slot.WriteTimestamp = request.LeaderId;
                slot.CompareAndSwapValue = request.Value;

                // Acceptors send the information to Learners
                SendDecideRequest(slot.WriteTimestamp, request.Value);
            }

            return new AcceptedReply
            {
                Slot = request.Slot,
                WriteTimestamp = slot.WriteTimestamp,
                Value = slot.CompareAndSwapValue,
            };
     
        }

        public DecideReply DecidePaxos(DecideRequest request)
        {
            while (!this.slots.ContainsKey(request.Slot))
            {
                // wait for slot to be created
            }

            SlotData slot = this.slots[request.Slot];

            Console.WriteLine($"Decide request in slot {request.Slot} ");

            lock (slot)
            {
                slot.DecidedReceived.Add((request.WriteTimestamp, request.Value));

                int majority = this.boneyHosts.Count / 2 + 1;

                Dictionary<(int,int), int> received = new Dictionary<(int,int), int>();
                foreach(var entry in slot.DecidedReceived)
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
                foreach(KeyValuePair<(int,int), int> kvp in received)
                {
                    // Learners have received a majority of accepted() with the same value
                    // Therefore the paxos has reached a consensus
                    if(kvp.Value >= majority)
                    {
                        slot.CurrentValue = kvp.Key.Item2;
                        slot.IsPaxosRunning = false;
                    }
                }

                // USEFULL TO PRINT DICTIONARIES
                //received.Select(i => $"{i.Key}: {i.Value}").ToList().ForEach(Console.WriteLine);

                return new DecideReply
                {
                    // empty ?
                };
            }
        }

        /*
         * Compare And Swap Service (Server) Implementation
         * Communication between Bank and Boney
         */

        public CompareAndSwapReply WaitForPaxosToEnd(SlotData slot, CompareAndSwapRequest request)
        {
            Console.WriteLine("Waiting for paxos to end.");
            while (slot.IsPaxosRunning)
            {
                // wait for paxos to end
            }

            Console.WriteLine($"Paxos ended with value {slot.CurrentValue}");

            return new CompareAndSwapReply
            {
                Slot = request.Slot,
                Outvalue = slot.CurrentValue,
            };
        }

        public CompareAndSwapReply CompareAndSwap(CompareAndSwapRequest request)
        {
            lock (this)
            {
                while (!this.slots.ContainsKey(request.Slot))
                {
                    // wait for slot to be created
                }

                SlotData slot = this.slots[request.Slot];

                Console.WriteLine($"Compare and swap request with value {request.Invalue} in slot {request.Slot}");

                // needs better names
                // CompareAndSwapValue -> valor que foi trocado pelo banco
                // CurrentValue -> Valor que foi decido pelo paxos
                if (slot.CompareAndSwapValue == -1 && slot.CurrentValue == -1)
                {
                    slot.CompareAndSwapValue = request.Invalue;
                    slot.IsPaxosRunning = true;
                }
                else
                {
                    return WaitForPaxosToEnd(slot, request);
                }

                // Do paxos


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
                    // TODO: All processes are frozen, what to do ?
                }

                Console.WriteLine($"I'm {this.processId} and Paxos Leader is {leader}");

                if (this.processId != leader)
                {
                    return WaitForPaxosToEnd(slot, request);
                }

                // Send prepare to all acceptors
                List<PromiseReply> promiseResponses = SendPrepareRequest();

                // Stop being leader if there is a more recent one
                foreach (var response in promiseResponses)
                {
                    if(response.ReadTimestamp > slot.ReadTimestamp)
                        return WaitForPaxosToEnd(slot, request);
                }

                // Get values from promises
                int mostRecent = -1;
                int valueToPropose = -1;
                foreach (var response in promiseResponses)
                {
                    if(response.ReadTimestamp > mostRecent)
                    {
                        mostRecent = response.ReadTimestamp;
                        valueToPropose = response.Value;
                    }
                }

                // If acceptors have no value, send own value
                if(valueToPropose == -1)
                    valueToPropose = slot.CompareAndSwapValue;

                // Send accept to all acceptors which will send decide to learners
                List<AcceptedReply> acceptResponses = SendAcceptRequest(valueToPropose);

                // Wait for learners to decide
                return WaitForPaxosToEnd(slot, request);
            }
        }
    }
}
