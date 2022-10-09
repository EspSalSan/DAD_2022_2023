using Grpc.Net.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Utilities;

namespace BankClient
{
    using BankHosts = Dictionary<int, Bank.BankClient>;

    internal class Program
    {
        static void SendDepositRequest(int processId, int clientSequenceNumber, string[] commandArgs, BankHosts bankHosts)
        {
            // Verify arguments
            if (commandArgs.Length != 2)
            {
                Console.WriteLine("Invalid number of arguments.");
                return;
            }
            if (!int.TryParse(commandArgs[1], out int value) || value < 0)
            {
                Console.WriteLine("Value must be a positive integer.");
                return;
            }

            clientSequenceNumber++;

            DepositRequest depositRequest = new DepositRequest 
            { 
                ClientId = processId,
                ClientSequenceNumber = clientSequenceNumber,
                Value = value,
            };

            // Send request to all bank processes
            List<Task> tasks = new List<Task>();
            foreach (var host in bankHosts)
            {
               Task t =  Task.Run(() => {
                   try
                   {
                       DepositReply depositReply = host.Value.Deposit(depositRequest);
                       Console.WriteLine(
                           $"Balance {depositReply.Balance} ({(depositReply.Primary ? "primary" : "secondary")})"
                       );

                   }
                   catch (Grpc.Core.RpcException e)
                   {
                       Console.WriteLine(e.Status);
                   }

                   return Task.CompletedTask;
               });

               tasks.Add(t);
            }

            // Clients wait for only one response
            Task.WaitAny(tasks.ToArray());
        }

        static void SendWithdrawRequest(int processId, int clientSequenceNumber, string[] commandArgs, BankHosts bankHosts)
        {
            // Verify arguments
            if (commandArgs.Length != 2)
            {
                Console.WriteLine("Invalid number of arguments");
            }
            if (!int.TryParse(commandArgs[1], out int value) || value < 0)
            {
                Console.WriteLine("Value must be a positive integer.");
                return;
            }

            clientSequenceNumber++;

            WithdrawRequest withdrawRequest = new WithdrawRequest {
                ClientId = processId,
                ClientSequenceNumber = clientSequenceNumber,
                Value = value 
            };

            // Send request to all bank processes
            List<Task> tasks = new List<Task>();
            foreach (var host in bankHosts)
            {
                Task t = Task.Run(() => {
                    try
                    {
                        WithdrawReply withdrawReply = host.Value.Withdraw(withdrawRequest);
                        Console.WriteLine(
                           $"Withdrew {withdrawReply.Value} | Balance {withdrawReply.Balance} ({(withdrawReply.Primary ? "primary" : "secondary")})"
                        );
                    }
                    catch (Grpc.Core.RpcException e)
                    {
                        Console.WriteLine(e.Status);
                    }

                    return Task.CompletedTask;
                });

                tasks.Add(t);
            }

            // Clients wait for only one response
            Task.WaitAny(tasks.ToArray());
        }

        static void SendReadBalanceRequest(int processId, int clientSequenceNumber, string[] commandArgs, BankHosts bankHosts)
        {
            // Verify arguments
            if (commandArgs.Length != 1)
            {
                Console.WriteLine("Invalid number of arguments.");
            }

            ReadRequest readRequest = new ReadRequest {
                ClientId = processId,
                ClientSequenceNumber = clientSequenceNumber,
            };

            // Send request to all bank processes
            List<Task> tasks = new List<Task>();
            foreach (var host in bankHosts)
            {
                Task t = Task.Run(() => {
                    try
                    {
                        ReadReply readReply = host.Value.Read(readRequest);
                        Console.WriteLine(
                           $"Balance {readReply.Balance} ({(readReply.Primary ? "primary" : "secondary")})"
                        );
                    }
                    catch (Grpc.Core.RpcException e)
                    {
                        Console.WriteLine(e.Status);
                    }

                    return Task.CompletedTask;
                });

                tasks.Add(t);
            }

            // Clients wait for only one response
            Task.WaitAny(tasks.ToArray());
        }


        static void Main(string[] args)
        {
            AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

            // Command Line Arguments
            int processId = int.Parse(args[0]);

            // Data from config file
            BoneyBankConfig config = Common.ReadConfig();

            BankHosts bankHosts = config.BankServers.ToDictionary(key => key.Id, value =>
            {
                GrpcChannel channel = GrpcChannel.ForAddress(value.Address);
                return new Bank.BankClient(channel);
            });

            int clientSequenceNumber = 0;

            Console.WriteLine($"Bank Client ({processId})");

            while (true) {

                string line = Console.ReadLine();
                string[] commandArgs = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                if (commandArgs.Length == 0) { continue; }

                switch (commandArgs[0])
                {
                    case "D":
                        SendDepositRequest(processId, clientSequenceNumber, commandArgs, bankHosts);
                        break;

                    case "W":
                        SendWithdrawRequest(processId, clientSequenceNumber, commandArgs, bankHosts);
                        break;

                    case "R":
                        SendReadBalanceRequest(processId, clientSequenceNumber, commandArgs, bankHosts);
                        break;

                    default:
                        Console.WriteLine("Command '" + commandArgs[0] + "' not found.");
                        break;
                }
            }
        }
    }
}
