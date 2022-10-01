using Grpc.Net.Client;
using System;
using System.Collections.Generic;
using System.IO;

namespace BankClient
{
    internal class Program
    {
        static string GetSolutionDir()
        {
            // Leads to /BoneyBank
            return Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory).Parent?.Parent?.Parent?.Parent?.FullName;
        }

        static void Main(string[] args)
        {
            AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

            Console.WriteLine(args);
            
            int processId = int.Parse(args[0]);

            // Read config.txt
            string baseDirectory = GetSolutionDir();
            string configFilePath = Path.Join(baseDirectory, "PuppetMaster", "config.txt");

            string[] lines = File.ReadAllLines(configFilePath);
            Dictionary <string, BankClientService.BankClientServiceClient> bankHosts = new Dictionary <string,BankClientService.BankClientServiceClient>();

            foreach (string line in lines)
            {
                string[] configArgs = line.Split(" ");

                if (configArgs[0].Equals("P") && configArgs[2].Equals("bank"))
                {
                    GrpcChannel channel = GrpcChannel.ForAddress(configArgs[3]);
                    BankClientService.BankClientServiceClient client = new BankClientService.BankClientServiceClient(channel);
                    bankHosts.Add(configArgs[1], client);
                }
            }

            Console.WriteLine("Starting Bank Client with" + processId);

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


                        // IS THIS PARALLEL ?
                        foreach (var entry in bankHosts)
                        {
                            try
                            {
                                DepositReply depositReply = entry.Value.Deposit(depositRequest, deadline: DateTime.UtcNow.AddSeconds(2));
                                Console.WriteLine("reply: ");
                                Console.WriteLine("\tBalance: " + depositReply.Balance);
                            }
                            catch (Grpc.Core.RpcException e)
                            {
                                Console.WriteLine(e.Status);
                            }
                            
                        }

                        break;

                    case "W":

                        if (commandArgs.Length != 2)
                        {
                            Console.WriteLine("Invalid number of arguments");
                        }

                        WithdrawRequest withdrawRequest = new WithdrawRequest { Value = int.Parse(commandArgs[1]) };

                        foreach (var entry in bankHosts)
                        {
                            WithdrawReply withdrawReply = entry.Value.Withdraw(withdrawRequest);
                            Console.WriteLine("reply: ");
                            Console.WriteLine("\tBalance: " + withdrawReply.Balance);
                        }

                        break;

                    case "R":

                        if (commandArgs.Length != 1)
                        {
                            Console.WriteLine("Invalid number of arguments");
                        }

                        ReadRequest readRequest = new ReadRequest { };

                        foreach (var entry in bankHosts)
                        {
                            ReadReply readReply = entry.Value.Read(readRequest);
                            Console.WriteLine("reply: ");
                            Console.WriteLine("\tBalance: " + readReply.Balance);
                        }

                        break;

                    default:
                        Console.WriteLine("Unknown Command");
                        break;
                }
            }
        }
    }
}
