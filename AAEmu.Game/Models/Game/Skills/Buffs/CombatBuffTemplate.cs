namespace AAEmu.Game.Models.Game.Skills.Buffs
{
    public class CombatBuffTemplate
    {
        public uint Id { get; set; }
        // unused
        public uint HitSkillId { get; set; }
        public SkillHitType HitType { get; set; }
        // unused
        public uint ReqSkillId { get; set; }
        public uint BuffId { get; set; }
        public bool BuffFromSource { get; set; }
        public bool BuffToSource { get; set; }
        public uint ReqBuffId { get; set; }
        public bool IsHealSpell { get; set; }
    }
}
