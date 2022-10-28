using System;
using System.Diagnostics;
using System.IO;
using Utilities;

namespace PuppetMaster
{
    internal class Program
    {
        static string GetSolutionDir()
        {
            // Leads to /BoneyBank
            return Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory).Parent?.Parent?.Parent?.Parent?.FullName;
        }

        static Process StartProcess(string path, string args)
        {
            ProcessStartInfo pInfo = new ProcessStartInfo();

            pInfo.UseShellExecute = true;
            pInfo.CreateNoWindow = false;
            pInfo.WindowStyle = ProcessWindowStyle.Normal;
            pInfo.FileName = path;
            pInfo.Arguments = args;

            return Process.Start(pInfo);
        }

        static Process CreateProcess(string[] configArgs)
        {
            // Not MacOS friendly because it compiles to .dll and not .exe
            string baseDirectory = GetSolutionDir();
            string clientPath = Path.Combine(baseDirectory, "BankClient", "bin", "Debug", "netcoreapp3.1", "BankClient.exe");
            string serverPath = Path.Combine(baseDirectory, "BankServer", "bin", "Debug", "netcoreapp3.1", "BankServer.exe");
            string boneyPath = Path.Combine(baseDirectory, "Boney", "bin", "Debug", "netcoreapp3.1", "Boney.exe");

            string id = configArgs[1];
            string name = configArgs[2];
            
            if (name.Equals("client"))
            {
                string script = configArgs[3];
                return StartProcess(clientPath, id + " " + script);
            }
            else if (name.Equals("boney") || name.Equals("bank"))
            {
                string url = configArgs[3].Remove(0, 7);
                string host = url.Split(':')[0];
                string port = url.Split(':')[1];

                Console.WriteLine(id + " " + host + " " + port);

                if (name.Equals("bank"))
                {
                    return StartProcess(serverPath, id + " " + host + " " + port);
                }
                else
                {
                    return StartProcess(boneyPath, id + " " + host + " " + port);
                }
            }
            else
            {
                Console.WriteLine("Incorrect config file.");
                return null;
            }
        }

        static void Main()
        {
            string baseDirectory = Common.GetSolutionDir();
            string configFilePath = Path.Join(baseDirectory, "PuppetMaster", "config.txt");

            if (!File.Exists(configFilePath))
            {
                Console.WriteLine("Config file not found.");
                return;
            }

            foreach (string line in File.ReadAllLines(configFilePath))
            {
                string[] configArgs = line.Split(" ");

                if (configArgs[0].Equals("P"))
                    CreateProcess(configArgs);
            }
        }
    }
}
