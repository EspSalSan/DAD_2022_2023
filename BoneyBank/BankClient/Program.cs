using Grpc.Net.Client;
using System;
using System.ComponentModel;

namespace BankClient
{
    internal class Program
    {
        static void Main(string[] args)
        {

            AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

            /*
             * Configuration for client should follow:
             *  P <id> client
             * e.g.
             *  P 1 client 
             */

            // verification for configuration file (probably not needed)
            
            // this must come from a configuration file
            int processId = -1;
            const string serverHost = "localhost:1001";

            if (args.Length != 3)
            {
                Console.WriteLine("Wrong number of arguments.");
            }
            else if (!args[0].Equals("P"))
            {
                Console.WriteLine("idk.");
            }
            else if (int.TryParse(args[1], out processId) && processId > 0)
            {
                Console.WriteLine("Process id must be a valid positive integer.");
            }
            else if (args[2].Equals("client"))
            {
                Console.WriteLine("Can only be started as 'client'.");
            }

            GrpcChannel channel = GrpcChannel.ForAddress("http://" + serverHost);
            BankServerService.BankServerServiceClient client = new BankServerService.BankServerServiceClient(channel);

            Console.WriteLine("Starting Bank Client on " + serverHost + "with process id " + processId);

            while (true) {

                string line = Console.ReadLine();
                string[] commandArgs = line.Split(" ");

                if (commandArgs.Length == 0) { continue; }

                switch (commandArgs[0])
                {
                    case "D":

                        if (commandArgs.Length != 2)
                        {
                            Console.WriteLine("Invalid number of arguments");
                        }

                        DepositRequest depositRequest = new DepositRequest { Value = int.Parse(commandArgs[1]) };
                        DepositReply depositReply = client.Deposit(depositRequest);
                        Console.WriteLine("reply: ");
                        Console.WriteLine("\tBalance: " + depositReply.Balance);
                        break;

                    case "W":

                        if (commandArgs.Length != 2)
                        {
                            Console.WriteLine("Invalid number of arguments");
                        }

                        WithdrawRequest withdrawRequest = new WithdrawRequest { Value = int.Parse(commandArgs[1]) };
                        WithdrawReply withdrawReply = client.Withdraw(withdrawRequest);
                        Console.WriteLine("reply: ");
                        Console.WriteLine("\tValue " + withdrawReply.Value);
                        Console.WriteLine("\tBalance: " + withdrawReply.Balance);
                        break;

                    case "R":

                        if (commandArgs.Length != 1)
                        {
                            Console.WriteLine("Invalid number of arguments");
                        }

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
        }
    }
}
