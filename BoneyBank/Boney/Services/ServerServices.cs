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

        /*
        * Paxos Service (Client/Server) Implementation
        * Communication between Boney and Boney
        */

        // TODO RESTO DOS COMANDOS DO PAXOS
        public PromiseReply PreparePaxos(PrepareRequest request)
        {

            Console.WriteLine($"Prepare request from {request.LeaderId} in slot {request.Slot} ");

            int slotNumber = request.Slot;
            SlotData slot = this.slots[slotNumber];

            // Verify if we can do this or if we should lock(this) and the entire function
            lock (slot)
            {
                if (slot.ReadTimestamp < request.LeaderId)
                {
                    slot.ReadTimestamp = request.LeaderId;
                }
                return new PromiseReply
                {
                    Slot = slotNumber,
                    ReadTimestamp = slot.ReadTimestamp,
                    Value = slot.CompareAndSwapValue,
                };
            }
        }

        public AcceptedReply AcceptPaxos(AcceptRequest request)
        {
            Console.WriteLine($"Accept request from {request.LeaderId} in slot {request.Slot} ");

            int slotNumber = request.Slot;
            SlotData slot = this.slots[slotNumber];

            lock (slot)
            {
                if (slot.WriteTimestamp < request.LeaderId)
                {
                    slot.WriteTimestamp = request.LeaderId;
                    slot.CompareAndSwapValue = request.Value;
                }
                return new AcceptedReply
                {
                    Slot = slotNumber,
                    WriteTimestamp = slot.WriteTimestamp,
                    Value = slot.CompareAndSwapValue,
                };
            }
        }

        public DecideReply DecidePaxos(DecideRequest request)
        {
            Console.WriteLine($"Decide request from {request.LeaderId} in slot {request.Slot} ");

            int slotNumber = request.Slot;
            SlotData slot = this.slots[slotNumber];

            lock (slot)
            {

                slot.DecidedReceived.Add(request.writeTimestamp);

                int majority = this.boneyHosts.Count / 2 + 1;


                return new DecideReply
                {

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

                // needs better names
                // CompareAndSwapValue -> valor que foi trocado pelo banco
                // CurrentValue -> Valor que foi decido pelo paxos
                if (slot.CompareAndSwapValue == -1 && slot.CurrentValue == -1)
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

                Console.WriteLine($"Paxos Leader is {leader} and I'm {this.processId}");

                // If self is leader, send prepare(processId)

                if (this.processId != leader)
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

                PrepareRequest prepareRequest = new PrepareRequest
                {
                    Slot = this.currentSlot,
                    LeaderId = this.processId
                };

                List<PromiseReply> promiseResponses = new List<PromiseReply>();

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

                // Wait for promise(readTS, value) from majority

                // If another leader is running, wait for him to finish paxos
                foreach(var response in promiseResponses)
                {
                    if(response.ReadTimestamp > slot.ReadTimestamp)
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
                }

                // Get most recent value from received

                int valueToPropose = -1;
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
                if(valueToPropose == -1)
                {
                    valueToPropose = slot.CompareAndSwapValue;
                }

                // Send accept(processId, value)

                AcceptRequest acceptRequest = new AcceptRequest
                {
                    Slot = this.currentSlot,
                    LeaderId = this.processId,
                    Value = valueToPropose
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

                // BIG TODO URGENT: PASSAR ISTO PARA O ACCEPT()

                // If another leader is running, wait for him to finish paxos
                foreach (var response in acceptResponses)
                {
                    if (response.WriteTimestamp > slot.WriteTimestamp)
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
                }

                // SEND DECIDE para isPaxosRunning
                DecideRequest decideRequest = new DecideRequest
                {
                    Slot = this.currentSlot,
                    LeaderId = this.processId,
                    Value = valueToPropose
                };

                foreach (var host in this.boneyHosts)
                {
                    try
                    {
                        DecideReply decideReply = host.Value.Decide(acceptRequest);
                    }
                    catch (Grpc.Core.RpcException e)
                    {
                        Console.WriteLine(e.Status);
                    }
                }
                // CONFIRMAR COM PROFESSOR SE O DECIDE "AINDA É PAXOS"

                // If received, value is accepted

                return new CompareAndSwapReply
                {
                    Slot = request.Slot,
                    Outvalue = slot.CurrentValue,
                };
            }
        }
    }
}
