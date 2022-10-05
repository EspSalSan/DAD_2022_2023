using BankServer.Services;
using Grpc.Core;
using Grpc.Net.Client;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Utilities;

namespace BankServer
{
    internal class Program
    {
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
            BoneyBankConfig config = Common.ReadConfig();
            int numberOfProcesses = config.NumberOfProcesses;
            Dictionary<string, TwoPhaseCommit.TwoPhaseCommitClient> bankHosts = config.BankServers.ToDictionary(
                key => key.Id.ToString(), 
                value => new TwoPhaseCommit.TwoPhaseCommitClient(GrpcChannel.ForAddress(value.Address))
            );
            Dictionary<string, CompareAndSwap.CompareAndSwapClient> boneyHosts = config.BoneyServers.ToDictionary(
                key => key.Id.ToString(),
                value => new CompareAndSwap.CompareAndSwapClient(GrpcChannel.ForAddress(value.Address))
            );
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
