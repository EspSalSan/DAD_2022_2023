using BankServer;
using Grpc.Core;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BankServer
{
    public class ServerService : BankServerService.BankServerServiceBase
    {
        private int balance;
        public ServerService()
        {
            this.balance = 0;
        }

        public override Task<WithdrawReply> Withdraw(
            WithdrawRequest request, ServerCallContext context)
        {
            return Task.FromResult(WithdrawMoney(request));
        }

        public WithdrawReply WithdrawMoney(WithdrawRequest request)
        {
            Console.WriteLine($"Withdraw: {request.Value}");
            this.balance -= request.Value;
            return new WithdrawReply
            {
                Value = request.Value,
                Balance = this.balance
            };
        }

        public override Task<DepositReply> Deposit(
            DepositRequest request, ServerCallContext context)
        {
            return Task.FromResult(DpMoney(request));
        }

        public DepositReply DpMoney(DepositRequest request)
        {
            Console.WriteLine($"Deposit: {request.Value}");
            this.balance += request.Value;
            return new DepositReply
            {
                Balance = this.balance
            };
        }

        public override Task<ReadReply> Read(
            ReadRequest request, ServerCallContext context)
        {
            return Task.FromResult(RdBalance(request));
        }

        public ReadReply RdBalance(ReadRequest request)
        {
            Console.WriteLine($"Read: {this.balance}");
            return new ReadReply
            {
                Balance = this.balance
            };
        }
    }
    class Program
    {
        const int Port = 1001;
        static void Main(string[] args)
        {
            Server server = new Server
            {
                Services = { BankServerService.BindService(new ServerService()) },
                Ports = { new ServerPort("localhost", Port, ServerCredentials.Insecure) }
            };
            server.Start();
            Console.WriteLine("ChatServer server listening on port " + Port);
            Console.WriteLine("Press any key to stop the server...");
            Console.ReadKey();

            server.ShutdownAsync().Wait();

        }
    }
}
