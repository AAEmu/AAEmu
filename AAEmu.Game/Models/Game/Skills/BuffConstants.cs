namespace AAEmu.Game.Models.Game.Skills;

public enum BuffConstants : uint
{
    ArmorBuffTag = 145,
    EquipmentBuffTag = 156,
    Untouchable = 545,
    NpcReturn = 550, // NPC returning home
    Cloth4P = 713,
    Cloth7P = 714,
    Leather4P = 715,
    Leather7P = 716,
    Plate4P = 717,
    Plate7P = 740,
    DualwieldProficiency = 831,
    FallStun = 1391, // From fall damage
    Bloodlust = 1482, // Ctrl+F
    Retribution = 2167,
    RemovalDebuff = 2250, // for houses
    LoggedOn = 2423, // player is logging in
    Dash = 2675,
    Deterioration = 3553, // Deterioration
    TaxProtection = 3554, // Tax Protection
    EquipDualwield = 4899,
    ScoreMemorized = 6010,
    FlutePlay = 6176,
    LutePlay = 6177,
    DrumPlay = 8216, // this one is actually called Play Drums, but not really used
    EquipShield = 8226,
    EquipTwoHanded = 8227,
    InBeautySalon = 6117,
    SearchSchoolOfFish = 5736,
    Overburdened = 831, // SustainBuff - Carrying heavy objects reduces movement speed and prevents teleporting or gliding.
    MasterOwnership = 4867  // Vehicle ownership buff, prevents non-owners from attaching to the vehicle.
   // Overburdened = 7221
}
