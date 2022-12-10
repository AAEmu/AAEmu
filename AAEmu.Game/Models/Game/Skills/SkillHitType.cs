namespace AAEmu.Game.Models.Game.Skills
{
    public enum SkillHitType : byte
    {
        Invalid = 0,
        MeleeHit = 1,
        MeleeCritical = 3,
        MeleeMiss = 4,
        MeleeDodge = 5,
        MeleeBlock = 6,
        MeleeParry = 7,
        RangedHit = 9,
        RangedMiss = 10,
        RangedCritical = 11, // aka CriticalHealHit
        SpellHit = 13, // aka HealHit
        SpellMiss = 014,
        SpellCritical = 15,
        RangedDodge = 16,
        RangedBlock = 17,
        Immune = 18,
        SpellResist = 19,
        RangedParry = 20
    }
}
