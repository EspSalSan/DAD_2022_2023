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
        private int value;
        private int clientId;
        private CommandType type;
        private int sequenceNumber;
        private int clientSequenceNumber;

        public ClientCommand(int slot, int clientId, int clientSequenceNumber, int sequenceNumber, CommandType type, int value)
        {
            this.slot = slot;
            this.clientId = clientId;
            this.clientSequenceNumber = clientSequenceNumber;
            this.sequenceNumber = sequenceNumber;
            this.type = type;
            this.value = value;
        }

        public int Slot { get => slot; set => this.slot = value; }
        public int Value { get => value; set => this.value = value; }
        public CommandType Type { get => type; set => type = value; }
        public int ClientId { get => clientId; set => clientId = value; }
        public int ClientSequenceNumber { get => clientSequenceNumber; set => clientSequenceNumber = value; }
        public int SequenceNumber { get => sequenceNumber; set => sequenceNumber = value; }
    }
}
