using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Utilities
{
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
    
    public struct ProcessState
    {
        public bool Frozen { get; }
        public bool Suspected { get; }
        
        public ProcessState(bool frozen, bool suspected)
        {
            Frozen = frozen;
            Suspected = suspected;
        }
    }

    public struct BoneyBankConfig
    {
        public List<BankProcess> BankServers { get; }
        public List<BankProcess> BoneyServers { get; }
        public int NumberOfProcesses { get; }
        
        public (int, TimeSpan) SlotDetails { get; }
        
        public Dictionary<int, ProcessState>[] ProcessStates { get; }

        public BoneyBankConfig(List<BankProcess> bankServers, List<BankProcess> boneyServers, int numberOfProcesses, int slotDuration, TimeSpan startTime, Dictionary<int, ProcessState>[] processStates)
        {
            BankServers = bankServers;
            BoneyServers = boneyServers;
            NumberOfProcesses = numberOfProcesses;
            SlotDetails = (slotDuration, startTime);
            ProcessStates = processStates;
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
            int slotDuration = -1;
            TimeSpan startTime = new TimeSpan();
            Dictionary<int, ProcessState>[] processStates = null;
            
            string pattern = @"(\([^0-9]*\d+[^0-9]*\))";
            Regex rg = new Regex(pattern);

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
                else if (args[0].Equals("T"))
                {
                    string[] time = args[1].Split(":");
                    startTime = new TimeSpan(int.Parse(time[0]), int.Parse(time[1]), int.Parse(time[2]));
                }
                else if (args[0].Equals("D"))
                {
                    slotDuration = int.Parse(args[1]);
                }
                else if (args[0].Equals("S"))
                {
                    int numberOfSlots = int.Parse(args[1]);
                    processStates = new Dictionary<int, ProcessState>[numberOfSlots];
                }
                else if (args[0].Equals("F"))
                {
                    if (processStates == null)
                    {
                        // TODO: invalid config, maybe throw an exception instead of ignoring
                        continue;
                    }
                    
                    MatchCollection matched = rg.Matches(line);
                    int slotId = int.Parse(args[1]);
                    processStates[slotId - 1] = new Dictionary<int, ProcessState>();
                    
                    foreach (Match match in matched)
                    {
                        string[] values = match.Value.Split(",");
                        int processId = int.Parse(values[0].Remove(0, 1));
                        bool frozen = values[1].Equals("F");
                        bool suspected = values[2].Remove(values[2].Length - 1).Equals("S");
                        processStates[slotId].Add(processId, new ProcessState(frozen, suspected));
                    }
                }
            }
            
            return new BoneyBankConfig(bankServers, boneyServers, numberOfProcesses, slotDuration, startTime, processStates);
        }
    }
}