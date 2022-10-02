using System;
using System.Collections.Generic;
using System.Text;

namespace Boney.Services
{
    public class ServerService
    {
        private int processId;
        private int currentValue;
        private int readTimestamp;
        private int writeTimestamp;

        public ServerService(int processId)
        {
            this.processId = processId;
            this.currentValue = -1;
            this.readTimestamp = -1;
            this.writeTimestamp = -1;
        }

        /*
         * Paxos Service Implementation
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


        public CompareAndSwapReply CompareAndSwapBoney(CompareAndSwapRequest request)
        {
            return null;
        }
    }
}
