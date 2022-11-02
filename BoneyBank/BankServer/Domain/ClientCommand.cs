namespace BankServer.Domain
{
    public enum CommandType
    {
        Deposit,
        Withdraw,
    }
    
    public class ClientCommand
    {
        private int slot;
        private decimal value;
        private int clientId;
        private bool success;
        private CommandType type;
        private int sequenceNumber;
        private int clientSequenceNumber;
        
        public ClientCommand(int slot, int clientId, int clientSequenceNumber, int sequenceNumber, CommandType type, decimal value)
        {
            this.slot = slot;
            this.type = type;
            this.value = value;
            this.success = false;
            this.clientId = clientId;
            this.sequenceNumber = sequenceNumber;
            this.clientSequenceNumber = clientSequenceNumber;
        }

        public int Slot { get => slot; set => this.slot = value; }
        public decimal Value { get => value; set => this.value = value; }
        public CommandType Type { get => type; set => type = value; }
        public int ClientId { get => clientId; set => clientId = value; }
        public int ClientSequenceNumber { get => clientSequenceNumber; set => clientSequenceNumber = value; }
        public int SequenceNumber { get => sequenceNumber; set => sequenceNumber = value; }
        public bool Success { get => success; set => success = value; }
    }
}
