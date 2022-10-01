using BankServer;
using Grpc.Net.Client;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace BankClient
{
    internal class Program
    {
        static string GetSolutionDir()
        {
            // Leads to /BoneyBank
            return Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory).Parent?.Parent?.Parent?.Parent?.FullName;
        }
        static Dictionary<string, BankService.BankServiceClient> GetBankHost(string[] lines) 
        {
            // Search config for bank URLs
            Dictionary<string, BankService.BankServiceClient> bankHosts = new Dictionary <string,BankService.BankServiceClient>();

            foreach (string line in lines)
            {
                string[] configArgs = line.Split(" ");

                if (configArgs[0].Equals("P") && configArgs[2].Equals("bank"))
                {
                    GrpcChannel channel = GrpcChannel.ForAddress(configArgs[3]);
                    bankHosts.Add(configArgs[1], new BankService.BankServiceClient(channel));
                }
            }

            return bankHosts;
        }

        static List<Dictionary<int, string>> GetProcessesState(string[] lines)
        {
            // Search config for process state (suspected / not-suspected)
            List<Dictionary<int, string>> processesStatePerSlot = new List<Dictionary<int, string>>();

            // Regex to detect the triplet, e.g.: (1, N, NS)
            string pattern = @"(\([^0-9]*\d+[^0-9]*\))";
            Regex rg = new Regex(pattern);

            foreach (string line in lines)
            {
                if (line[0].Equals('F'))
                {
                    var processesStateSlot = new Dictionary<int, string>(); 

                    MatchCollection matched = rg.Matches(line);
                    foreach (Match match in matched)
                    {
                        string[] values = match.Value.Split(", ");

                        int processId = int.Parse(values[0].Remove(0,1));
                        string state = values[2].Remove(values[2].Length-1);

                        processesStateSlot.Add(processId, state);  
                    }

                    // FUTURE USE PRINT DICTIONARY VERY USEFULL
                    //processesStateSlot.Select(i => $"{i.Key}: {i.Value}").ToList().ForEach(Console.WriteLine);
                    processesStatePerSlot.Add(processesStateSlot);
                }
            }

            return processesStatePerSlot;
        }

        static void SendDepositRequest(string[] commandArgs, Dictionary<string, BankService.BankServiceClient> bankHosts)
        {
            // Verify arguments
            if (commandArgs.Length != 2)
            {
                Console.WriteLine("Invalid number of arguments");
            }

            DepositRequest depositRequest = new DepositRequest { Value = int.Parse(commandArgs[1]) };

            // IS THIS PARALLEL ? no. SHOULD IT BE ?
            // Send request to all bank processes
            foreach (var entry in bankHosts)
            {
                try
                {
                    DepositReply depositReply = entry.Value.Deposit(depositRequest, deadline: DateTime.UtcNow.AddSeconds(2));
                    // Maybe use logging instead of console.writeline
                    Console.WriteLine("reply: ");
                    Console.WriteLine("\tBalance: " + depositReply.Balance);
                }
                catch (Grpc.Core.RpcException e)
                {
                    Console.WriteLine(e.Status);
                }
            }
        }

        static void SendWithdrawRequest(string[] commandArgs, Dictionary<string, BankService.BankServiceClient> bankHosts)
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
                    WithdrawReply withdrawReply = entry.Value.Withdraw(withdrawRequest, deadline: DateTime.UtcNow.AddSeconds(2));
                    Console.WriteLine("reply: ");
                    Console.WriteLine("\tBalance: " + withdrawReply.Balance);
                }
                catch (Grpc.Core.RpcException e)
                {
                    Console.WriteLine(e.Status);
                }
            }
        }

        static void SendReadBalanceRequest(string[] commandArgs, Dictionary<string, BankService.BankServiceClient> bankHosts)
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
                    ReadReply readReply = entry.Value.Read(readRequest, deadline: DateTime.UtcNow.AddSeconds(2));
                    Console.WriteLine("reply: ");
                    Console.WriteLine("\tBalance: " + readReply.Balance);
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

            // Read config.txt
            string baseDirectory = GetSolutionDir();
            string configFilePath = Path.Join(baseDirectory, "PuppetMaster", "config.txt");
            string[] lines = File.ReadAllLines(configFilePath);

            // Initial data
            int processId = int.Parse(args[0]);
            Dictionary <string, BankService.BankServiceClient> bankHosts = GetBankHost(lines);
            List<Dictionary<int, string>> processesStatePerSlot = GetProcessesState(lines);
            // Duvida: "Each process keeps a guess of who is the group leader" isto é em relacao ao lider dos bancos ou tbm do boney ?

            Console.WriteLine("Bank Client with id: " + processId);

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
