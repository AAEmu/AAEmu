namespace AAEmu.Game.Models.Game.Slaves
{
    public class SlavePassiveBuffs
    {
        public uint Id { get; set; }
        public uint OwnerId { get; set; }
        public string OwnerType { get; set; }
        public uint PassiveBuffId { get; set; }
    }
}
