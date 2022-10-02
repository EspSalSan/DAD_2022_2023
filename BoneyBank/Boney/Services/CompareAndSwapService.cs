using System;
using System.Collections.Generic;
using System.Text;

namespace Boney.Services
{
    public class CompareAndSwapService : CompareAndSwap.CompareAndSwapBase
    {
        private ServerService serverService;

        public CompareAndSwapService(ServerService serverService)
        {
            this.serverService = serverService;
        }
    }
}
