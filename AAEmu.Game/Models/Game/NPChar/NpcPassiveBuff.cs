using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.Game.Models.Game.NPChar
{
    public class NpcPassiveBuff
    {
        public uint Id { get; set; }
        public uint OwnerId { get; set; }
        public string OwnerType { get; set; }
        public uint PassiveBuffId { get; set; }
        public PassiveBuffTemplate PassiveBuff { get; set; }
    }
}
