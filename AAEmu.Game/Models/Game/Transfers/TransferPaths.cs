namespace AAEmu.Game.Models.Game.Transfers
{
    public class TransferPaths
    {
        public uint Id { get; set; }
        public uint OwnerId { get; set; }
        public string OwnerType { get; set; }
        public string PathName { get; set; }
        public double WaitTimeStart { get; set; }
        public double WaitTimeEnd { get; set; }
    }
}
