namespace AAEmu.Game.Models.Game.Transfers
{
    public class TransferBindings
    {
        public uint Id { get; set; }
        public uint OwnerId { get; set; }
        public string OwnerType { get; set; }
        public byte AttachPointId { get; set; }
        public uint TransferId { get; set; }
    }
}
