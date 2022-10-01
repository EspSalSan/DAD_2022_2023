using Grpc.Core;
using System;
using System.Collections.Generic;
using System.IO;

namespace Boney
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

            int processId = int.Parse(args[0]);
            string host = args[1];
            int port = int.Parse(args[2]);

            // Read config.txt
            string baseDirectory = GetSolutionDir();
            string configFilePath = Path.Join(baseDirectory, "PuppetMaster", "config.txt");

            string[] lines = File.ReadAllLines(configFilePath);
            Dictionary<string, string> boneyHosts = new Dictionary<string, string>();

            foreach (string line in lines)
            {
                string[] configArgs = line.Split(" ");

                if (configArgs[0].Equals("P") && configArgs[2].Equals("boney"))
                {
                    // todo
                    //GrpcChannel channel = GrpcChannel.ForAddress(configArgs[3]);
                    //BankServerService.BankServerServiceClient client = new BankServerService.BankServerServiceClient(channel);
                    boneyHosts.Add(configArgs[1], configArgs[3]);
                }
            }

            Server server = new Server
            {
                Services = { BoneyService.BindService(new ServerService(processId)) },
                Ports = { new ServerPort(host, port, ServerCredentials.Insecure) }
            };

            server.Start();

            Console.WriteLine("process id:" + processId);
            Console.WriteLine("ChatServer server listening on port " + args[2]);
            Console.WriteLine("Press any key to stop the server...");
            Console.ReadKey();

            server.ShutdownAsync().Wait();
        }
    }
}
