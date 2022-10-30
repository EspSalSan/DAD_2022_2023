using Grpc.Core;
using System.Threading.Tasks;

namespace BankServer.Services
{
    public class BankService : Bank.BankBase
    {
        private readonly ServerService serverService;

        public BankService(ServerService serverService)
        {
            this.serverService = serverService;
        }

        public override Task<WithdrawReply> Withdraw(WithdrawRequest request, ServerCallContext context)
        {
            return Task.FromResult(serverService.WithdrawMoney(request));
        }

        public override Task<DepositReply> Deposit(DepositRequest request, ServerCallContext context)
        {
            return Task.FromResult(serverService.DepositMoney(request));
        }

        public override Task<ReadReply> Read(ReadRequest request, ServerCallContext context)
        {
            return Task.FromResult(serverService.ReadBalance(request));
        }
    }
}
