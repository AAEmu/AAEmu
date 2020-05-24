namespace AAEmu.Game.Models.Game.Transfers
{
    public class TransferBindingDoodads
    {
        public uint Id { get; set; }
        public uint OwnerId { get; set; }
        public string OwnerType { get; set; }
        public int AttachPointId { get; set; }
        public uint DoodadId { get; set; }
    }
}
