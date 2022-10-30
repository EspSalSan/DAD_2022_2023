using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Utilities
{
    public struct ProcessInfo
    {
        public int Id { get; }
        public string Type { get; }
        public string Address { get; }

        public ProcessInfo(int id, string type, string address)
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
        public List<ProcessInfo> BankProcesses { get; }
        public List<ProcessInfo> BoneyProcesses { get; }
        public int NumberOfProcesses { get; }    
        public (int, TimeSpan) SlotDetails { get; }
        public Dictionary<int, ProcessState>[] ProcessStates { get; }

        public BoneyBankConfig(List<ProcessInfo> bankProcesses, List<ProcessInfo> boneyProcesses, int slotDuration, TimeSpan startTime, Dictionary<int, ProcessState>[] processStates)
        {
            BankProcesses = bankProcesses;
            BoneyProcesses = boneyProcesses;
            NumberOfProcesses = bankProcesses.Count + boneyProcesses.Count;
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
            // Read config file
            string configFilePath = Path.Join(GetSolutionDir(), "PuppetMaster", "config.txt");
            string[] lines = File.ReadAllLines(configFilePath);

            int slotDuration = -1;
            TimeSpan startTime = new TimeSpan();
            Dictionary<int, ProcessState>[] processStates = null;
            List<ProcessInfo> bankProcesses = new List<ProcessInfo>();
            List<ProcessInfo> boneyProcesses = new List<ProcessInfo>();

            Regex rg = new Regex(@"(\([^0-9]*\d+[^0-9]*\))");

            foreach (string line in lines)
            {
                string[] args = line.Split(" ");

                if (args[0].Equals("P") && !args[2].Equals("client"))
                {
                    int processId = int.Parse(args[1]);
                    ProcessInfo processInfo = new ProcessInfo(processId, args[2], args[3]);
                    switch (args[2])
                    {
                        case "bank":
                            bankProcesses.Add(processInfo);
                            break;
                        case "boney":
                            boneyProcesses.Add(processInfo);
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

                    foreach (Match match in matched.Cast<Match>())
                    {
                        string[] values = match.Value.Split(",");
                        int processId = int.Parse(values[0].Remove(0, 1));
                        bool frozen = values[1].Equals(" F");
                        bool suspected = values[2].Remove(values[2].Length - 1).Equals(" S");
                        processStates[slotId - 1].Add(processId, new ProcessState(frozen, suspected));
                    }
                }
            }
            return new BoneyBankConfig(bankProcesses, boneyProcesses, slotDuration, startTime, processStates);
        }
    }
}