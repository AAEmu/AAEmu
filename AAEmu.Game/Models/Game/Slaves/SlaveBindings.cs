namespace AAEmu.Game.Models.Game.Slaves
{
    public class SlaveBindings
    {
        public uint Id { get; set; }
        public uint OwnerId { get; set; }
        public string OwnerType { get; set; }
        public uint SlaveId { get; set; }
        public uint AttachPointId { get; set; }
    }
}
