using Grpc.Core;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Boney.Services
{
    public class PaxosService : Paxos.PaxosBase
    {
        private ServerService serverService;

        public PaxosService(ServerService serverService)
        {
            this.serverService = serverService;
        }

        public override Task<PromiseReply> Prepare(PrepareRequest request, ServerCallContext context)
        {
            return Task.FromResult(serverService.PreparePaxos(request));
        }
    }
}
