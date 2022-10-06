using System;
using System.Collections.Generic;
using System.Text;

namespace Boney.Domain
{
    public class SlotData
    {
        private int slot;
        private int compareAndSwapValue;

        private bool isPaxosRunning;

        // maybe switch to proposedValue
        private int currentValue;
        private int readTimestamp;
        private int writeTimestamp;
        

        public SlotData(int slot)
        {
            this.slot = slot;
            this.isPaxosRunning = false;
            this.currentValue = 0;
            this.readTimestamp = -1;
            this.writeTimestamp = -1;
            this.compareAndSwapValue = 0;
        }

        public int Slot { get => slot; set => slot = value; }
        public bool IsPaxosRunning { get => isPaxosRunning; set => isPaxosRunning = value; }
        public int CurrentValue { get => currentValue; set => currentValue = value; }
        public int ReadTimestamp { get => readTimestamp; set => readTimestamp = value; }
        public int WriteTimestamp { get => writeTimestamp; set => writeTimestamp = value; }
        public int CompareAndSwapValue { get => compareAndSwapValue; set => compareAndSwapValue = value; }

    }
}
