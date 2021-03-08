using AAEmu.Game.Models.Game.Skills.Static;

namespace AAEmu.Game.Models.Game.NPChar
{
    public class NpcSkill
    {
        public uint Id { get; set; }
        public uint OwnerId { get; set; }
        public string OwnerType { get; set; }
        public uint SkillId { get; set; }
        public SkillUseConditionKind SkillUseCondition { get; set; }
        public float SkillUseParam1 { get; set; }
        public float SkillUseParam2 { get; set; }
    }
}
