using Grpc.Net.Client;
using System;
using System.Collections.Generic;
using System.IO;
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

            Console.WriteLine($"({processId},{clientSequenceNumber}) Deposit {commandArgs[1]}");

            DepositRequest depositRequest = new DepositRequest 
            { 
                ClientId = processId,
                ClientSequenceNumber = clientSequenceNumber,
                Value = value,
            };

            // Send request to all bank processes
            bool primaryReplied = false;
            List<Task> tasks = new List<Task>();
            foreach (var host in bankHosts)
            {
               Task t =  Task.Run(() => {
                   try
                   {
                       DepositReply depositReply = host.Value.Deposit(depositRequest);
                       if (depositReply.Primary)
                       {
                           Console.WriteLine(
                               $"   ({processId},{clientSequenceNumber}) " +
                               $"Balance {depositReply.Balance} (primary)"
                            );
                           primaryReplied = true;

                       }
                       else
                       {
                           Console.WriteLine(
                               $"   ({processId},{clientSequenceNumber}) " +
                               $"(secondary)"
                            );
                       }
                       
                   }
                   catch (Grpc.Core.RpcException e)
                   {
                       Console.WriteLine(e.Status);
                   }

                   return Task.CompletedTask;
               });

               tasks.Add(t);
            }

            // Wait for primary reply
            while (!primaryReplied)
            {
                tasks.RemoveAt(Task.WaitAny(tasks.ToArray()));
            }
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

            Console.WriteLine($"({processId},{clientSequenceNumber}) Withdraw {commandArgs[1]}");

            WithdrawRequest withdrawRequest = new WithdrawRequest {
                ClientId = processId,
                ClientSequenceNumber = clientSequenceNumber,
                Value = value 
            };

            bool primaryReplied = false;
            // Send request to all bank processes
            List<Task> tasks = new List<Task>();
            foreach (var host in bankHosts)
            {
                Task t = Task.Run(() => {
                    try
                    {
                        WithdrawReply withdrawReply = host.Value.Withdraw(withdrawRequest);
                        if (withdrawReply.Primary)
                        {
                            Console.WriteLine(
                                $"   ({processId},{clientSequenceNumber}) " +
                                $"Withdrew {withdrawReply.Value} | Balance {withdrawReply.Balance} (primary)"
                             );
                            primaryReplied = true;
                        }
                        else
                        {
                            Console.WriteLine(
                                $"   ({processId},{clientSequenceNumber}) " +
                                $"(secondary)"
                             );
                        }
                    }
                    catch (Grpc.Core.RpcException e)
                    {
                        Console.WriteLine(e.Status);
                    }

                    return Task.CompletedTask;
                });

                tasks.Add(t);
            }

            // Wait for primary reply
            while (!primaryReplied)
            {
                tasks.RemoveAt(Task.WaitAny(tasks.ToArray()));
            }
        }

        static void SendReadBalanceRequest(int processId, int clientSequenceNumber, string[] commandArgs, BankHosts bankHosts)
        {
            // Verify arguments
            if (commandArgs.Length != 1)
            {
                Console.WriteLine("Invalid number of arguments.");
            }

            Console.WriteLine($"({processId}) Read");

            ReadRequest readRequest = new ReadRequest {
                ClientId = processId,
                ClientSequenceNumber = clientSequenceNumber,
            };

            bool primaryReplied = false;
            // Send request to all bank processes
            List<Task> tasks = new List<Task>();
            foreach (var host in bankHosts)
            {
                Task t = Task.Run(() => {
                    try
                    {
                        ReadReply readReply = host.Value.Read(readRequest);
                        if (readReply.Primary)
                        {
                            Console.WriteLine(
                                $"   ({processId},{clientSequenceNumber}) " +
                                $"Balance {readReply.Balance} (primary)"
                             );
                            primaryReplied = true;
                        }
                        else
                        {
                            Console.WriteLine(
                                $"   ({processId},{clientSequenceNumber}) " +
                                $"(secondary)"
                             );
                        }
                    }
                    catch (Grpc.Core.RpcException e)
                    {
                        Console.WriteLine(e.Status);
                    }

                    return Task.CompletedTask;
                });

                tasks.Add(t);
            }

            // Wait for primary reply
            while (!primaryReplied)
            {
                tasks.RemoveAt(Task.WaitAny(tasks.ToArray()));
            }
        }

        static void Sleep(string[] commandArgs)
        {
            // Verify arguments
            if (commandArgs.Length != 2)
            {
                Console.WriteLine("Invalid number of arguments.");
            }
            if (!int.TryParse(commandArgs[1], out int value) || value < 0)
            {
                Console.WriteLine("Value must be a positive integer.");
                return;
            }

            Console.WriteLine($"Sleeping for {value} milliseconds...");

            System.Threading.Thread.Sleep(value);

            Console.WriteLine("Done sleeping.");
        }

        static void Main(string[] args)
        {
            AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

            // Command Line Arguments
            int processId = int.Parse(args[0]);
            string scriptName = args[1];

            // Data from config file
            BoneyBankConfig config = Common.ReadConfig();

            // Process data from config file
            BankHosts bankHosts = config.BankProcesses.ToDictionary(key => key.Id, value =>
            {
                GrpcChannel channel = GrpcChannel.ForAddress(value.Address);
                return new Bank.BankClient(channel);
            });
            (int slotDuration, TimeSpan startTime) = config.SlotDetails;

            // Read client scripts
            string scriptFilePath = Path.Join("BankClient", "Scripts", scriptName+".txt");
            string[] lines = File.ReadAllLines(scriptFilePath);

            Console.WriteLine($"Bank Client ({processId})");

            // Wait for slots to start
            System.Threading.Thread.Sleep(startTime - DateTime.Now.TimeOfDay);

            int clientSequenceNumber = 0;

            foreach (string line in lines) { 

                string[] commandArgs = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                if (commandArgs.Length == 0) { continue; }

                switch (commandArgs[0])
                {
                    case "D":
                        clientSequenceNumber++;
                        SendDepositRequest(processId, clientSequenceNumber, commandArgs, bankHosts);
                        break;

                    case "W":
                        clientSequenceNumber++;
                        SendWithdrawRequest(processId, clientSequenceNumber, commandArgs, bankHosts);
                        break;

                    case "R":
                        // TODO: Devia ter um clientSequenceNumber unico e ainda nao tem, problemas com o Read do lado do banco
                        SendReadBalanceRequest(processId, clientSequenceNumber, commandArgs, bankHosts);
                        break;

                    case "S":
                        Sleep(commandArgs);
                        break;

                    default:
                        Console.WriteLine("Command '" + commandArgs[0] + "' not found.");
                        break;
                }
            }

            Console.ReadKey();
        }
    }
}
