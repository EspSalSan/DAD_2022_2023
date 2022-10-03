using Grpc.Core;
using System.Threading.Tasks;

namespace BankServer.Services
{
    public class TwoPhaseCommitService : TwoPhaseCommit.TwoPhaseCommitBase
    {
        private ServerService serverService;

        public TwoPhaseCommitService(ServerService serverService)
        {
            this.serverService = serverService;
        }

        public override Task<TentativeReply> Tentative(TentativeRequest request, ServerCallContext context)
        {
            return Task.FromResult(serverService.Tentative(request));
        }

        public override Task<CommitReply> Commit(CommitRequest request, ServerCallContext context)
        {
            return Task.FromResult(serverService.Commit(request));
        }
    }
}
