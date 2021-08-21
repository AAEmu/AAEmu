namespace AAEmu.Game.Models.Game.Skills
{
    public enum BuffConstants : uint
    {
        ARMOR_BUFF_TAG = 145,
        EQUIPMENT_BUFF_TAG = 156,
        Untouchable = 545,
        CLOTH_4P = 713,
        CLOTH_7P = 714,
        LEATHER_4P = 715,
        LEATHER_7P = 716,
        PLATE_4P = 717,
        PLATE_7P = 740,
        DUALWIELD_PROFICIENCY = 831,
        FallStun = 1391, // From fall damage
        BLOODLUST_BUFF = 1482,
        RETRIBUTION_BUFF = 2167,
        RemovalDebuff = 2250, // for houses
        LoggedOn = 2423, // player is logging in
        BuffDeterioration = 3553, // Deterioration
        BuffTaxprotection = 3554, // Tax Protection
        EQUIP_DUALWIELD_BUFF = 4899,
        EQUIP_SHIELD_BUFF = 8226,
        EQUIP_TWOHANDED_BUFF = 8227,
        NPC_RETURN_BUFF = 550 // TODO: Find
    }
}
