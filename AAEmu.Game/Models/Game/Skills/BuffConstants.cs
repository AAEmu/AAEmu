namespace AAEmu.Game.Models.Game.Skills;

#pragma warning disable IDE0079 // Remove unnecessary suppression
#pragma warning disable CA1069 // Enums should not have duplicate values

public enum BuffConstants : uint
{
    ArmorBuffTag = 145,
    EquipmentBuffTag = 156,
    Untouchable = 545,
    NpcReturn = 550, // NPC returning home
    WeakenedBody = 1128, // PvE death penalty
    RespawnCooldown = 2385, // 5 min cooldown after temple-revive
    WarZoneLeech = 4424, // PvP death penalty in War zones
    Prisoner_Nuian = 631,
    Cloth4P = 713,
    Cloth7P = 714,
    Leather4P = 715,
    Leather7P = 716,
    Plate4P = 717,
    Plate7P = 740,
    DualwieldProficiency = 831,
    FallStun = 1391, // From fall damage
    Bloodlust = 1482, // Ctrl+F
    Prisoner_Haranyan = 2028,
    Retribution = 2167,
    RemovalDebuff = 2250, // for houses
    LoggedOn = 2423, // player is logging in
    Dash = 2675,
    ForciblyAwaitingTrial = 3619,
    Jury = 3621,
    Trial_Defendant = 3623,
    Deterioration = 3553, // Deterioration
    TaxProtection = 3554, // Tax Protection
    Wanted = 3710, // CrimePoint >= 50
    Contemptuous = 4832, // Pirate
    SuspectedUser = 4862,
    PrimeSuspect = 4863,
    OwnersMark = 4867,  // Vehicle ownership buff, prevents non-owners from attaching to the vehicle.
    EquipDualwield = 4899,
    TransformingIntoPrimeSuspect = 4947,
    CourtHouse = 4970,
    SearchSchoolOfFish = 5736,
    ScoreMemorized = 6010,
    FlutePlay = 6176,
    LutePlay = 6177,
    InBeautySalon = 6117,
    CannotEscapeBuff = 6729,
    DrumPlay = 8216, // this one is actually called Play Drums, but not really used
    EquipShield = 8226,
    EquipTwoHanded = 8227,

    // Tags
    TagPrisoner = 344,
    TagOverburdened = 831, // SustainBuff - Carrying heavy objects reduces movement speed and prevents teleporting or gliding.
    TagWanted = 894,
    TagOffender = 1008,
    TagSuspects = 1035, // All Bot suspect related buffs have this tag once over the threshold
}
