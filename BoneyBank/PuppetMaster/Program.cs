﻿using System;
using System.Diagnostics;
using System.IO;

namespace PuppetMaster
{
    internal class Program
    {
        static string GetSolutionDir()
        {
            // Leads to /BoneyBank
            return Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory).Parent?.Parent?.Parent?.Parent?.FullName;
        }

        static void StartProcess(string path, string args)
        {
            ProcessStartInfo p_info = new ProcessStartInfo();
            p_info.UseShellExecute = true;
            p_info.CreateNoWindow = false;
            p_info.WindowStyle = ProcessWindowStyle.Normal;
            p_info.FileName = path;
            p_info.Arguments = args;
            Process.Start(p_info);
        }

        static void CreateProcess(string[] configArgs)
        {
            // Not MacOS friendly ;-)
            string baseDirectory = GetSolutionDir();
            string clientPath = Path.Combine(baseDirectory, "BankClient", "bin", "Debug", "netcoreapp3.1", "BankClient.exe");
            string serverPath = Path.Combine(baseDirectory, "BankServer", "bin", "Debug", "netcoreapp3.1", "BankServer.exe");
            string boneyPath = Path.Combine(baseDirectory, "Boney", "bin", "Debug", "netcoreapp3.1", "Boney.exe");

            string id = configArgs[1];
            string name = configArgs[2];

            if (name.Equals("client"))
            {
                StartProcess(clientPath, id);
            }
            else if (name.Equals("boney") || name.Equals("bank"))
            {
                string url = configArgs[3].Remove(0, 7);
                string host = url.Split(':')[0];
                string port = url.Split(':')[1];

                Console.WriteLine(id + " " + host + " " + port);

                if (name.Equals("bank"))
                {
                    StartProcess(serverPath, id + " " + host + " " + port);
                }
                else
                {
                    StartProcess(boneyPath, id + " " + host + " " + port);
                }
            }
            else
            {
                Console.WriteLine("Incorrect config file");
                return;
            }
        }

        static void Main(string[] args)
        {

            string baseDirectory = GetSolutionDir();
            string configFilePath = Path.Join(baseDirectory, "PuppetMaster", "config.txt");

            if (!File.Exists(configFilePath))
            {
                Console.WriteLine("Config file not found");
                return;
            }

            string[] lines = File.ReadAllLines(configFilePath);
            foreach(string line in lines)
            {
                string[] configArgs = line.Split(" ");

                switch (configArgs[0])
                {
                    case "P":
                        CreateProcess(configArgs);
                        break;

                    case "S":
                        break;

                    case "T":
                        break;

                    case "D":
                        break;

                    case "F":
                        break;

                    default:
                        break;

                }
            }    
        }
    }
}