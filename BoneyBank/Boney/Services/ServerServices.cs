using System;
using System.Collections.Generic;
using System.Text;

namespace Boney.Services
{
    public class ServerService
    {
        private int processId;

        public ServerService(int processId)
        {
            this.processId = processId;
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
                    ReadTimestamp = 1,
                    Value = 1
                };
            }
        }
    }
}
