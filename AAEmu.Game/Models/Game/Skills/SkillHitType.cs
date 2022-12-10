using System;
using System.Collections.Generic;
using System.Text;

namespace AAEmu.Game.Models.Game.Skills
{
    public enum SkillHitType
    {
        Invalid = 0x0,
        MeleeHit = 0x1,
        MeleeCritical = 0x3,
        MeleeMiss = 0x4,
        MeleeDodge = 0x5,
        MeleeBlock = 0x6,
        MeleeParry = 0x7,
        RangedHit = 0x9,
        RangedMiss = 0xA,
        RangedCritical = 0xB,
        SpellHit = 0xD,
        SpellMiss = 0xE,
        SpellCritical = 0xF,
        RangedDodge = 0x10,
        RangedBlock = 0x11,
        Immune = 0x12,
        SpellResist = 0x13,
        RangedParry = 0x14,
    }
}
