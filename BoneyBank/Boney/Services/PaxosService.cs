using Grpc.Core;
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

        public override Task<AcceptedReply> Accept(AcceptRequest request, ServerCallContext context)
        {
            return Task.FromResult(serverService.AcceptPaxos(request));
        }

        public override Task<DecideReply> Decide(DecideRequest request, ServerCallContext context)
        {
            return Task.FromResult(serverService.DecidePaxos(request));
        }
    }
}
