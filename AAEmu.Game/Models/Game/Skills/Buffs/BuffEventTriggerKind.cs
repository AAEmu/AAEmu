using System;
using System.Collections.Generic;
using System.Text;

namespace AAEmu.Game.Models.Game.Skills.Buffs
{
    public enum BuffEventTriggerKind
    {
        Attack = 0x1, 
        Attacked = 0x2, 
        Damage = 0x3, 
        Damaged = 0x4, 
        Dispelled = 0x5, 
        Timeout = 0x6, 
        DamagedMelee = 0x7,
        DamagedRanged = 0x8, 
        DamagedSpell = 0x9, 
        DamagedSiege = 0xa, 
        Landing = 0xb, 
        Started = 0xc, 
        RemoveOnMove = 0xd, 
        ChannelingCancel = 0xe, 
        RemoveOnDamage = 0xf, 
        Death = 0x10, 
        Unmount = 0x11, 
        Kill = 0x12, 
        DamagedCollision = 0x13, 
        Immotality = 0x14, 
        Time = 0x15, 
        KillAny = 0x16
    }
}
