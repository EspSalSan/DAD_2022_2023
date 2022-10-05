using Grpc.Net.Client;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Utilities;

namespace BankClient
{
    using BankHosts = Dictionary<int, Bank.BankClient>;

    internal class Program
    {
        static void SendDepositRequest(string[] commandArgs, BankHosts bankHosts)
        {
            // Verify arguments
            if (commandArgs.Length != 2)
            {
                Console.WriteLine("Invalid number of arguments");
            }

            DepositRequest depositRequest = new DepositRequest { Value = int.Parse(commandArgs[1]) };

            // Send request to all bank processes
            foreach (var entry in bankHosts)
            {
                try
                {
                    DepositReply depositReply = entry.Value.Deposit(depositRequest);
                    Console.WriteLine($"Deposit response: {depositReply.Balance}");
                }
                catch (Grpc.Core.RpcException e)
                {
                    Console.WriteLine(e.Status);
                }
            }
        }

        static void SendWithdrawRequest(string[] commandArgs, BankHosts bankHosts)
        {
            // Verify arguments
            if (commandArgs.Length != 2)
            {
                Console.WriteLine("Invalid number of arguments");
            }

            WithdrawRequest withdrawRequest = new WithdrawRequest { Value = int.Parse(commandArgs[1]) };

            // Send request to all bank processes
            foreach (var entry in bankHosts)
            {
                try
                {
                    WithdrawReply withdrawReply = entry.Value.Withdraw(withdrawRequest);
                    Console.WriteLine($"Withdraw response: {withdrawReply.Balance}");
                }
                catch (Grpc.Core.RpcException e)
                {
                    Console.WriteLine(e.Status);
                }
            }
        }

        static void SendReadBalanceRequest(string[] commandArgs, BankHosts bankHosts)
        {
            // Verify arguments
            if (commandArgs.Length != 1)
            {
                Console.WriteLine("Invalid number of arguments");
            }

            ReadRequest readRequest = new ReadRequest { };

            // Send request to all bank processes
            foreach (var entry in bankHosts)
            {
                try
                {
                    ReadReply readReply = entry.Value.Read(readRequest);
                    Console.WriteLine($"Read balance response: {readReply.Balance}");
                }
                catch (Grpc.Core.RpcException e)
                {
                    Console.WriteLine(e.Status);
                }
            }
        }


        static void Main(string[] args)
        {
            AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

            // Initial data
            int processId = int.Parse(args[0]);
            BoneyBankConfig config = Common.ReadConfig();
            BankHosts bankHosts = config.BankServers.ToDictionary(key => key.Id, value =>
            {
                GrpcChannel channel = GrpcChannel.ForAddress(value.Address);
                return new Bank.BankClient(channel);
            });

            Console.WriteLine($"Bank Client ({processId}) starting...");

            while (true) {

                string line = Console.ReadLine();
                string[] commandArgs = line.Split(" ");

                if (commandArgs.Length == 0) { continue; }

                switch (commandArgs[0])
                {
                    case "D":
                        SendDepositRequest(commandArgs, bankHosts);
                        break;

                    case "W":
                        SendWithdrawRequest(commandArgs, bankHosts);
                        break;

                    case "R":
                        SendReadBalanceRequest(commandArgs, bankHosts);
                        break;

                    default:
                        Console.WriteLine("Command '" + commandArgs[0] + "' not found.");
                        break;
                }
            }
        }
    }
}
