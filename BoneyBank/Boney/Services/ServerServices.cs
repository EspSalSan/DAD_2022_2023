using System;
using System.Collections.Generic;
using System.Text;

namespace Boney.Services
{
    public class ServerService
    {
        
        private int processId;
        // Maybe create a PaxosDetails class with all this information per slot
        private bool isPaxosRunning;
        private int currentValue;
        private int readTimestamp;
        private int writeTimestamp;
        private int compareAndSwapValue;

        public ServerService(int processId)
        {
            this.processId = processId;
            this.currentValue = -1;
            this.readTimestamp = -1;
            this.writeTimestamp = -1;
            this.compareAndSwapValue = -1;
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
                    ReadTimestamp = this.readTimestamp,
                    Value = this.currentValue
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
                    WriteTimestamp = this.writeTimestamp,
                    Value = this.currentValue
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
                if (this.compareAndSwapValue == -1)
                {
                    this.compareAndSwapValue = request.Invalue;
                }
                else
                {
                    while (this.isPaxosRunning)
                    {
                        // wait for paxos to end
                    }
                }

                // Do paxos

                return new CompareAndSwapReply
                {
                    Slot = request.Slot,
                    Outvalue = this.currentValue,
                };
            }
        }
    }
}
