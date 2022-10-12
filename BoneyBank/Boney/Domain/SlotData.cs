using System.Collections.Generic;

namespace Boney.Domain
{
    public class SlotData
    {
        private int slot;
        private bool isPaxosRunning;

        private int decidedValue;   // Value decided by Paxos (final)
        private int readTimestamp;
        private int writeTimestamp;
        private int writtenValue;   // Value written by Paxos client (not final)

        // Learners keep a list of decided values to know when a majority was
        // achieved and reply to the client with the final value
        private List<(int, int)> decidedReceived;

        public SlotData(int slot)
        {
            this.slot = slot;
            this.isPaxosRunning = false;
            
            this.decidedValue = -1;
            this.readTimestamp = -1;
            this.writeTimestamp = -1;
            this.writtenValue = -1;
            
            this.decidedReceived = new List<(int, int)>();
        }

        public int Slot { get => slot; set => slot = value; }
        public bool IsPaxosRunning { get => isPaxosRunning; set => isPaxosRunning = value; }
        public int DecidedValue { get => decidedValue; set => decidedValue = value; }
        public int ReadTimestamp { get => readTimestamp; set => readTimestamp = value; }
        public int WriteTimestamp { get => writeTimestamp; set => writeTimestamp = value; }
        public int WrittenValue { get => writtenValue; set => writtenValue = value; }
        public List<(int, int)> DecidedReceived { get => decidedReceived; set => decidedReceived = value; }
    }
}
