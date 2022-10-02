using Grpc.Core;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Boney.Services
{
    public class CompareAndSwapService : CompareAndSwap.CompareAndSwapBase
    {
        private ServerService serverService;

        public CompareAndSwapService(ServerService serverService)
        {
            this.serverService = serverService;
        }

        public override Task<CompareAndSwapReply> CompareAndSwap(CompareAndSwapRequest request, ServerCallContext context)
        {
            return Task.FromResult(serverService.CompareAndSwapBoney(request));
        }
    }
}
