using Grpc.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace BankServer
{
    public class ServerService : BankService.BankServiceBase
    {
        private int processId;
        private int balance;
        public ServerService(int processId)
        {
            this.balance = 0;
            this.processId = processId;
        }

        // USE LOCKS !!

        public override Task<WithdrawReply> Withdraw(WithdrawRequest request, ServerCallContext context)
        {
            return Task.FromResult(WithdrawMoney(request));
        }

        public WithdrawReply WithdrawMoney(WithdrawRequest request)
        {
            lock (this)
            {
                Console.WriteLine($"Withdraw: {request.Value}");

                this.balance -= request.Value;

                return new WithdrawReply
                {
                    Value = request.Value,
                    Balance = this.balance
                };
            } 
        }

        public override Task<DepositReply> Deposit(DepositRequest request, ServerCallContext context)
        {
            return Task.FromResult(DepositMoney(request));
        }

        public DepositReply DepositMoney(DepositRequest request)
        {
            lock (this)
            {
                Console.WriteLine($"Deposit: {request.Value}");
                this.balance += request.Value;
                return new DepositReply
                {
                    Balance = this.balance
                };
            }
        }

        public override Task<ReadReply> Read(ReadRequest request, ServerCallContext context)
        {
            return Task.FromResult(ReadBalance(request));
        }

        public ReadReply ReadBalance(ReadRequest request)
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
        static string GetSolutionDir()
        {
            // Leads to /BoneyBank
            return Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory).Parent?.Parent?.Parent?.Parent?.FullName;
        }

        static void Main(string[] args)
        {
            // wtf does this do
            AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

            Console.WriteLine(args);

            // Data from config file
            int processId = int.Parse(args[0]);
            string host = args[1];
            int port = int.Parse(args[2]);

            // TODO
            int numberOfSlots;
            int startTime; // best type to store hh:mm:ss ?
            int interval; 
            // somehow store data about being frozen/not frozen


            // Read config.txt
            string baseDirectory = GetSolutionDir();
            string configFilePath = Path.Join(baseDirectory, "PuppetMaster", "config.txt");
            
            string[] lines = File.ReadAllLines(configFilePath);
            Dictionary <string, string> boneyHosts = new Dictionary <string, string>();

            foreach (string line in lines)
            {
                string[] configArgs = line.Split(" ");

                if (configArgs[0].Equals("P") && configArgs[2].Equals("boney"))
                {
                    // todo
                    //GrpcChannel channel = GrpcChannel.ForAddress(configArgs[3]);
                    //BankServerService.BankServerServiceClient client = new BankServerService.BankServerServiceClient(channel);
                    boneyHosts.Add(configArgs[1], configArgs[3]);
                }
            }

            Server server = new Server
            {
                Services = { BankService.BindService(new ServerService(processId)) },
                Ports = { new ServerPort(host, port, ServerCredentials.Insecure) }
            };

            server.Start();

            Console.WriteLine("process id:" + processId);
            Console.WriteLine("ChatServer server listening on port " + args[2]);
            Console.WriteLine("Press any key to stop the server...");
            Console.ReadKey();

            server.ShutdownAsync().Wait();

        }
    }
}
