using AAEmu.Game.Models.Game.DoodadObj.Static;

namespace AAEmu.Game.Models.Game.Mate
{
    public class MountAttachedSkills
    {
        public uint Id { get; set; }
        public uint MountSkillId { get; set; }
        public AttachPointKind AttachPointId { get; set; }
        public uint SkillId { get; set; }
    }
}