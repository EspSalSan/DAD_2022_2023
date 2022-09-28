using Grpc.Core;
using Grpc.Net.Client;
using System;
using System.Linq;

namespace BankClient
{
    internal class Program
    {
        static void Main(string[] args)
        {
            const int ServerPort = 1001;
            const string ServerHostname = "localhost";
            AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

            GrpcChannel channel = GrpcChannel.ForAddress("http://" + ServerHostname + ":" + ServerPort);
            var client = new BankServerService.BankServerServiceClient(channel);
            while (true) {
                string line;
                line = Console.ReadLine();
                string[] commandArgs = line.Split(" ");

                switch (commandArgs[0])
                {
                    case "D":
                        DepositRequest depositRequest = new DepositRequest { Value = int.Parse(commandArgs[1]) };
                        DepositReply depositReply = client.Deposit(depositRequest);
                        Console.WriteLine("reply: ");
                        Console.WriteLine("\tBalance: " + depositReply.Balance);
                        break;
                    case "W":
                        WithdrawRequest withdrawRequest = new WithdrawRequest { Value = int.Parse(commandArgs[1]) };
                        WithdrawReply withdrawReply = client.Withdraw(withdrawRequest);
                        Console.WriteLine("reply: ");
                        Console.WriteLine("\tValue " + withdrawReply.Value);
                        Console.WriteLine("\tBalance: " + withdrawReply.Balance);
                        break;
                    case "R":
                        ReadRequest readRequest = new ReadRequest { };
                        ReadReply readReply = client.Read(readRequest);
                        Console.WriteLine("reply: ");
                        Console.WriteLine("\tBalance: " + readReply.Balance);
                        break;
                    default:
                        Console.WriteLine("Unknown Command");
                        break;

                }
            }
            Console.ReadKey();
            Console.WriteLine("Bank Client Process");
            Console.ReadKey();
        }
    }
}
