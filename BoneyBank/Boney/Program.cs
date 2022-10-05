using Boney.Services;
using Grpc.Core;
using Grpc.Net.Client;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Utilities;

namespace Boney
{
    internal class Program
    {
        static private void PrepareSlot(ServerService serverService)
        {
            Console.WriteLine("Starting new slot...");
            // switch  Normal <-> Freeze
            // idk what else
            // avancar com o slot: currentSlot += 1
        }

        static private void SetSlotTimer(TimeSpan time, int slotDuration, ServerService serverService)
        {
            TimeSpan timeToGo = time - DateTime.Now.TimeOfDay;
            if (timeToGo < TimeSpan.Zero)
            {
                Console.WriteLine("Slot starting before finished server setup.");
                Console.WriteLine("Aborting...");
                return;
            }

            new System.Threading.Timer(x =>
            {
                PrepareSlot(serverService);
            }, null, (int)timeToGo.TotalMilliseconds, slotDuration);
        }

        static void Main(string[] args)
        {
            AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

            // Initial data
            int processId = int.Parse(args[0]);
            string host = args[1];
            int port = int.Parse(args[2]);
            BoneyBankConfig config = Common.ReadConfig();
            int numberOfProcesses = config.NumberOfProcesses;
            Dictionary<int, Paxos.PaxosClient> boneyHosts = config.BoneyServers.ToDictionary(
                key => key.Id,
                value => new Paxos.PaxosClient(GrpcChannel.ForAddress(value.Address))
            );
            
            List<Dictionary<int, bool>> processesStatePerSlot = config.ProcessStates.Select(
                states => states.ToDictionary(key => key.Key, value => value.Value.Suspected)
            ).ToList();
            (int slotDuration, TimeSpan startTime) = config.SlotDetails;

            ServerService serverService = new ServerService(processId);

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
            timeSpan += TimeSpan.FromSeconds(5);

            // Starts thread in timeSpan
            SetSlotTimer(timeSpan, slotDuration, serverService);

            Console.WriteLine("Press any key to stop the server...");
            Console.ReadKey();

            server.ShutdownAsync().Wait();
        }
    }
}
