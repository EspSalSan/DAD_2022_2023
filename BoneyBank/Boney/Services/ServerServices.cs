using Boney.Domain;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

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

        public void PrepareSlot()
        {
            if (this.currentSlot >= processFrozenPerSlot.Count)
            {
                Console.WriteLine("No more slots to process.");
                Console.WriteLine("Aborting...");
                return;
            }

            this.currentSlot += 1;

            Console.WriteLine($"Preparing slot {this.currentSlot}");

            SlotData slot = new SlotData(this.currentSlot);
            slots.TryAdd(this.currentSlot, slot);

            // TODO é preciso fazer alguma coisa no Boney quando começa um slot ?

            Console.WriteLine("Preparation ended.");
        }

            /*
             * Paxos Service (Client/Server) Implementation
             * Communication between Boney and Boney
             */

            // TODO RESTO DOS COMANDOS DO PAXOS
            public PromiseReply PreparePaxos(PrepareRequest request)
        {
            lock (this)
            {
                Console.WriteLine($"Prepare request from {request.LeaderId} ");

                return new PromiseReply
                {
                    ReadTimestamp = -1,
                    Value = -1
                };
            }
        }

        public AcceptReply PorposePaxos(PorposeRequest request)
        {
            lock (this)
            {
                Console.WriteLine($"Porpose request from {request.LeaderId} ");

                return new AcceptReply
                {
                    WriteTimestamp = -1,
                    Value = -1
                };
            }
        }

        /*
         * Compare And Swap Service (Server) Implementation
         * Communication between Bank and Boney
         */

        public CompareAndSwapReply CompareAndSwap(CompareAndSwapRequest request)
        {
            lock (this)
            {
                // Variables depend on the slot to prevent confusion
                // if multiple paxos are running for different slots

                int slotNumber = request.Slot;
                SlotData slot = this.slots[slotNumber];

                if (slot.CompareAndSwapValue == -1)
                {
                    slot.CompareAndSwapValue = request.Invalue;
                    slot.IsPaxosRunning = true;
                }
                else // compareAndSwapValue is set which means paxos might be running (or not)
                {
                    while (slot.IsPaxosRunning)
                    {
                        // wait for paxos to end
                    }

                    return new CompareAndSwapReply
                    {
                        Slot = request.Slot,
                        Outvalue = slot.CurrentValue,
                    };
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
                    {
                        leader = process.Key;
                    }
                }

                if (leader == int.MaxValue)
                {
                    // something went wrong, all processes are frozen
                    // abort ? stall ?
                }

                // If self is leader, send prepare(processId)

                if (this.processId != leader)
                {
                    // Does nothing and end prepareSlot
                }

                PrepareRequest prepareRequest = new PrepareRequest
                {
                    LeaderId = this.processId
                };

                List<PromiseReply> promiseResponses = new List<PromiseReply>();

                foreach (var entry in this.boneyHosts)
                {
                    try
                    {
                        PromiseReply promiseReply = entry.Value.Prepare(prepareRequest);
                        promiseResponses.Add(promiseReply);
                    }
                    catch (Grpc.Core.RpcException e)
                    {
                        Console.WriteLine(e.Status);
                    }
                }

                // Wait for promise(writeTS, value) from majority

                // Get most recent value from received

                int valueToPropose = 0;
                int mostRecent = -1;
                foreach (var response in promiseResponses)
                {
                    if(response.ReadTimestamp > mostRecent)
                    {
                        mostRecent = response.ReadTimestamp;
                        valueToPropose = response.Value;
                    }
                }

                // If all values are 0, send own value

                // Confirm what value is default (not sure if its zero)
                if(valueToPropose == 0)
                {
                    valueToPropose = request.Invalue;
                }

                // Send accept(processId, value)

                // Wait for accepted(processId, value) from majority

                // If received, value is accepted


                slot.IsPaxosRunning = false;

                return new CompareAndSwapReply
                {
                    Slot = request.Slot,
                    Outvalue = slot.CurrentValue,
                };
            }
        }
    }
}
