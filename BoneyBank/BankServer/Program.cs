using BankServer.Services;
using Grpc.Core;
using Grpc.Net.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using Utilities;

namespace BankServer
{
    internal class Program
    {
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
            AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
            
            // Command Line Arguments
            int processId = int.Parse(args[0]);
            string host = args[1];
            int port = int.Parse(args[2]);
            
            // Data from config file
            BoneyBankConfig config = Common.ReadConfig();

            int numberOfProcesses = config.NumberOfProcesses;
            (int slotDuration, TimeSpan startTime) = config.SlotDetails;
            Dictionary<int, TwoPhaseCommit.TwoPhaseCommitClient> bankHosts = config.BankServers.ToDictionary(
                key => key.Id, 
                value => new TwoPhaseCommit.TwoPhaseCommitClient(GrpcChannel.ForAddress(value.Address))
            );
            Dictionary<int, CompareAndSwap.CompareAndSwapClient> boneyHosts = config.BoneyServers.ToDictionary(
                key => key.Id,
                value => new CompareAndSwap.CompareAndSwapClient(GrpcChannel.ForAddress(value.Address))
            );
            List<Dictionary<int, bool>> processesSuspectedPerSlot = config.ProcessStates.Select(states =>
            {
                return states.ToDictionary(key => key.Key, value => value.Value.Suspected);
            }).ToList();
            List<bool> processFrozenPerSlot = config.ProcessStates.Select(states => states[processId].Frozen).ToList();

            // A process should not suspect itself (it knows if its frozen or not)
            for (int i = 0; i < processesSuspectedPerSlot.Count; i++)
                processesSuspectedPerSlot[i][processId] = processFrozenPerSlot[i];

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
            Console.WriteLine($"Working with {bankHosts.Count} banks and {boneyHosts.Count} boney processes");

            // Setting timeSpan to 5 seconds from Now just for testing
            TimeSpan timeSpan = DateTime.Now.TimeOfDay;
            timeSpan += TimeSpan.FromSeconds(5);

            // Starts thread in timeSpan
            SetSlotTimer(timeSpan, slotDuration * 1000, serverService);

            Console.WriteLine("Press any key to stop the server...");
            Console.ReadKey();

            server.ShutdownAsync().Wait();
        }
    }
}
