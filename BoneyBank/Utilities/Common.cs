using System;
using System.Collections.Generic;
using System.IO;

namespace Utilities
{
    // BankProcess struct with 3 fields (process id, type, and address)
    public struct BankProcess
    {
        public int Id { get; }
        public string Type { get; }
        public string Address { get; }

        // Constructor
        public BankProcess(int id, string type, string address)
        {
            Id = id;
            Type = type;
            Address = address;
        }
    }

    public struct BoneyBankConfig
    {
        public List<BankProcess> BankServers { get; }
        public List<BankProcess> BoneyServers { get; }
        public int NumberOfProcesses { get; }
        
        public BoneyBankConfig(List<BankProcess> bankServers, List<BankProcess> boneyServers, int numberOfProcesses)
        {
            BankServers = bankServers;
            BoneyServers = boneyServers;
            NumberOfProcesses = numberOfProcesses;
        }
    }
    
    public static class Common
    {
        public static string GetSolutionDir()
        {
            // Leads to /BoneyBank
            return Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory).Parent?.Parent?.Parent?.Parent?.FullName;
        }

        public static BoneyBankConfig ReadConfig()
        {
            string configFilePath = Path.Join(GetSolutionDir(), "PuppetMaster", "config.txt");
            string[] lines = File.ReadAllLines(configFilePath);
            int numberOfProcesses = 0;
            List<BankProcess> bankServers = new List<BankProcess>();
            List<BankProcess> boneyServers = new List<BankProcess>();

            foreach (string line in lines)
            {
                string[] args = line.Split(" ");

                if (args[0].Equals("P") && !args[2].Equals("client"))
                {
                    numberOfProcesses++;
                    int processId = int.Parse(args[1]);
                    BankProcess bankProcess = new BankProcess(processId, args[2], args[3]);
                    switch (args[2])
                    {
                        case "bank":
                            bankServers.Add(bankProcess);
                            break;
                        case "boney":
                            boneyServers.Add(bankProcess);
                            break;
                    }
                }
            }
            
            return new BoneyBankConfig(bankServers, boneyServers, numberOfProcesses);
        }
    }
}