using System;
using System.Collections.Generic;
using System.Text;

namespace BankServer
{
    public class TwoPhaseCommitService : TwoPhaseCommit.TwoPhaseCommitBase
    {
        private ServerService serverService;

        public TwoPhaseCommitService (ServerService serverService)
        {
            this.serverService = serverService;
        }
    }
}
