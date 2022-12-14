using Grpc.Core;
using System.Threading.Tasks;

namespace BankServer.Services
{
    public class TwoPhaseCommitService : TwoPhaseCommit.TwoPhaseCommitBase
    {
        private readonly ServerService serverService;

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

        public override Task<ListPendingRequestsReply> ListPendingRequests(ListPendingRequestsRequest request, ServerCallContext context)
        {
            return Task.FromResult(serverService.ListPendingRequests(request));
        }
    }
}
