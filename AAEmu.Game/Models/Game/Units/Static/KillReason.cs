namespace AAEmu.Game.Models.Game.Units.Static
{
    public enum KillReason
    {
        Unknown = 0x0,
        Damage = 0x1,
        PvpSiege = 0x2,
        PvpBattleField = 0x3,
        PvpHonorWar = 0x4,
        PvpEnemy = 0x5,
        PvpAlly = 0x6,
        CrimeDamage = 0x7,
        Fall = 0x8,
        Collide = 0x9,
        Capture = 0xA,
        Bomb = 0xB,
        NpcRemovalOnly = 0xC,
        Gm = 0xD,
        PortalTimeout = 0xE,
        PortalLinkBroken = 0xF,
        Drowning = 0x10,
        BadGuardTower = 0x11,
        SlaveEquipmentRandomDestroy = 0x12,
        SiegeEnd = 0x13,
    }
}
