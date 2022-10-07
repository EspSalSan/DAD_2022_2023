using Boney.Services;
using Grpc.Core;
using Grpc.Net.Client;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Utilities;

namespace Boney
{
    internal class Program
    {
        static private void SetSlotTimer(TimeSpan time, int slotDuration, ServerService serverService)
        {
            TimeSpan timeToGo = time - DateTime.Now.TimeOfDay;
            if (timeToGo < TimeSpan.Zero)
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
            Dictionary<int, Paxos.PaxosClient> boneyHosts = config.BoneyServers.ToDictionary(
                key => key.Id,
                value => new Paxos.PaxosClient(GrpcChannel.ForAddress(value.Address))
            );
            List<Dictionary<int, bool>> processesSuspectedPerSlot = config.ProcessStates.Select(states =>
            {
                return states.ToDictionary(key => key.Key, value => value.Value.Suspected);
            }).ToList();
            List<bool> processFrozenPerSlot = config.ProcessStates.Select(states => states[processId].Frozen).ToList();

            ServerService serverService = new ServerService(processId, processFrozenPerSlot, processesSuspectedPerSlot, boneyHosts);

            Server server = new Server
            {
                Services = { 
                    Paxos.BindService(new PaxosService(serverService)),
                    CompareAndSwap.BindService(new CompareAndSwapService(serverService))
                },
                Ports = { new ServerPort(host, port, ServerCredentials.Insecure) }
            };

            server.Start();

            Console.WriteLine($"Boney ({processId}) listening on port {port}");
            Console.WriteLine($"First slot starts at {startTime} with intervals of {slotDuration}");
            Console.WriteLine($"Working with {numberOfProcesses} processes ({boneyHosts.Count} boneys)");

            // Setting timeSpan to 5 seconds from Now just for testing
            TimeSpan timeSpan = DateTime.Now.TimeOfDay;
            timeSpan += TimeSpan.FromSeconds(7);

            // Starts thread in timeSpan
            SetSlotTimer(timeSpan, slotDuration * 1000, serverService);

            Console.WriteLine("Press any key to stop the server...");
            Console.ReadKey();

            server.ShutdownAsync().Wait();
        }
    }
}
