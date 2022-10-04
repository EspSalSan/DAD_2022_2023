using Grpc.Core;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace BankServer.Services
{
    public class CleanupService : Cleanup.CleanupBase
    {
        private ServerService serverService;

        public CleanupService(ServerService serverService)
        {
            this.serverService = serverService;
        }

        public override Task<ListPendingRequestsReply> Cleanup(ListPendingRequestsRequest request, ServerCallContext context)
        {
            return Task.FromResult(serverService.Cleanup(request));
        }
    }
}
