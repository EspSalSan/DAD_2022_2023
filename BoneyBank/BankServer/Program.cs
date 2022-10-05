﻿using BankServer.Services;
using Grpc.Core;
using Grpc.Net.Client;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Utilities;

namespace BankServer
{
    internal class Program
    {
        static int GetNumberOfProcesses(string[] lines)
        {
            int numberOfProcesses = 0;

            foreach (string line in lines)
            {
                string[] configArgs = line.Split(" ");

                if (configArgs[0].Equals("P") && (configArgs[2].Equals("boney") || configArgs[2].Equals("bank")))
                {
                    numberOfProcesses += 1;
                }
            }
            return numberOfProcesses;
        }

        static Dictionary<string, TwoPhaseCommit.TwoPhaseCommitClient> GetBankHost(string[] lines)
        {
            // Search config for bank URLs
            var bankHosts = new Dictionary<string, TwoPhaseCommit.TwoPhaseCommitClient>();

            foreach (string line in lines)
            {
                string[] configArgs = line.Split(" ");

                if (configArgs[0].Equals("P") && configArgs[2].Equals("bank"))
                {
                    GrpcChannel channel = GrpcChannel.ForAddress(configArgs[3]);
                    bankHosts.Add(configArgs[1], new TwoPhaseCommit.TwoPhaseCommitClient(channel));
                }
            }
            return bankHosts;
        }

        static Dictionary<string, CompareAndSwap.CompareAndSwapClient> GetBoneyHost(string[] lines)
        {
            // Search config for boney URLs
            var boneyHosts = new Dictionary<string, CompareAndSwap.CompareAndSwapClient>();

            foreach (string line in lines)
            {
                string[] configArgs = line.Split(" ");

                if (configArgs[0].Equals("P") && configArgs[2].Equals("boney"))
                {
                    GrpcChannel channel = GrpcChannel.ForAddress(configArgs[3]);
                    boneyHosts.Add(configArgs[1], new CompareAndSwap.CompareAndSwapClient(channel));
                }
            }
            return boneyHosts;
        }

        static List<Dictionary<int, bool>> GetProcessesSuspected(string[] lines)
        {
            // Search config for processes state (suspected / not-suspected)
            List<Dictionary<int, bool>> processesSuspectedPerSlot = new List<Dictionary<int, bool>>();

            // Regex to detect the triplet, e.g.: (1, N, NS)
            string pattern = @"(\([^0-9]*\d+[^0-9]*\))";
            Regex rg = new Regex(pattern);

            foreach (string line in lines)
            {
                if (line[0].Equals('F'))
                {
                    var processesStateSlot = new Dictionary<int, bool>();

                    MatchCollection matched = rg.Matches(line);
                    foreach (Match match in matched)
                    {
                        string[] values = match.Value.Split(", ");

                        int processId = int.Parse(values[0].Remove(0, 1));
                        string state = values[2].Remove(values[2].Length - 1);

                        processesStateSlot.Add(processId, state.Equals("S"));
                    }

                    // FUTURE USE PRINT DICTIONARY VERY USEFULL
                    //processesStateSlot.Select(i => $"{i.Key}: {i.Value}").ToList().ForEach(Console.WriteLine);
                    processesSuspectedPerSlot.Add(processesStateSlot);
                }
            }
            return processesSuspectedPerSlot;
        }

        static List<bool> GetProcessStatePerSlot(string[] lines, int currentProcessId)
        {
            // Search config for process state across slots
            List<bool> processFrozenPerSlot = new List<bool>();

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

                        int processId = int.Parse(values[0].Remove(0, 1));

                        if(currentProcessId == processId)
                        {
                            string state = values[1];
                            processFrozenPerSlot.Add(state.Equals("F"));
                        }
                    }
                }
            }
            return processFrozenPerSlot;
        }

        static (int slotDuration, TimeSpan startTime) GetSlotsDetails(string[] lines)
        {
            int slotDuration = -1;
            TimeSpan startTime = new TimeSpan();

            foreach (string line in lines)
            {
                if (line[0].Equals('T'))
                {
                    string[] configArgs = line.Split(" ");
                    string[] time = configArgs[1].Split(":");
                    startTime = new TimeSpan(int.Parse(time[0]), int.Parse(time[1]), int.Parse(time[2]));
                }
                if (line[0].Equals('D'))
                {
                    string[] configArgs = line.Split(" ");
                    slotDuration = int.Parse(configArgs[1]);
                }
            }
            return (slotDuration, startTime);
        }


        static private void SetSlotTimer(TimeSpan time, int slotDuration, ServerService serverService)
        {
            TimeSpan timeToGo = time - DateTime.Now.TimeOfDay;
            if(timeToGo < TimeSpan.Zero)
            {
                Console.WriteLine("Slot starting before finished server setup.");
                Console.WriteLine("Aborting...");
                Environment.Exit(0);
                return;
            }

            new System.Threading.Timer(x =>
            {
                serverService.PrepareSlot();
            }, null, (int)timeToGo.TotalMilliseconds, slotDuration);
        }

        static void Main(string[] args)
        {
            /* TODO
             * Onde guardar as funcoes de cliente ? (sendo que as funcoes de servidor ja estao no Services/
             * Talvez criar uma biblioteca para guardar as funcoes de ler a config
             */

            AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

            // Read config.txt
            string baseDirectory = Common.GetSolutionDir();
            string configFilePath = Path.Join(baseDirectory, "PuppetMaster", "config.txt");
            string[] lines = File.ReadAllLines(configFilePath);

            // Command Line Arguments
            int processId = int.Parse(args[0]);
            string host = args[1];
            int port = int.Parse(args[2]);
            // Data from config file
            int numberOfProcesses = GetNumberOfProcesses(lines);
            Dictionary<string, TwoPhaseCommit.TwoPhaseCommitClient> bankHosts = GetBankHost(lines);
            Dictionary<string, CompareAndSwap.CompareAndSwapClient> boneyHosts = GetBoneyHost(lines);
            List<Dictionary<int, bool>> processesSuspectedPerSlot = GetProcessesSuspected(lines);
            List<bool> processFrozenPerSlot = GetProcessStatePerSlot(lines, processId);
            (int slotDuration, TimeSpan startTime) = GetSlotsDetails(lines);

            // Provavelmente devia receber mais informacao
            ServerService serverService = new ServerService(processId, processFrozenPerSlot, processesSuspectedPerSlot, bankHosts, boneyHosts);

            Server server = new Server
            {
                Services = { 
                    Bank.BindService(new BankService(serverService)),
                    TwoPhaseCommit.BindService(new TwoPhaseCommitService(serverService)),
                },
                Ports = { new ServerPort(host, port, ServerCredentials.Insecure) }
            };

            server.Start();

            Console.WriteLine($"Bank Server ({processId}) listening on port {port}");
            Console.WriteLine($"First slot starts at {startTime} with intervals of {slotDuration}");
            Console.WriteLine($"Working with {numberOfProcesses} processes ({bankHosts.Count} banks and {boneyHosts.Count} boneys)");

            // Setting timeSpan to 5 seconds from Now just for testing
            TimeSpan timeSpan = DateTime.Now.TimeOfDay;
            timeSpan += TimeSpan.FromSeconds(5);

            // Starts thread in timeSpan
            SetSlotTimer(timeSpan, slotDuration, serverService);

            Console.WriteLine("Press any key to stop the server...");
            Console.ReadKey();

            server.ShutdownAsync().Wait();
        }
    }
}
