namespace AAEmu.Game.Models.Game.Items;

public enum SlotType : byte
{
    Invalid = 0,            // None = 0
    Equipment = 1,          // Equipment = 1,
    Bag = 2,                // Inventory = 2,
    Bank = 3,               // Bank = 3,
    Coffer = 4,             // Trade = 4,
    Seized = 5,
    Auction = 6,
    Mountable = 64,
    Mountable1Bag = 65,
    Mountable2Bag = 66,
    ShortcutAction = 235,
    ContributionPoint = 236,
    PetBattleEquipment = 237,
    PetBattleCommand = 238,
    PetBattleAction = 239,
    AutoUseAaPoint = 240,
    AaPoint = 241,
    SlaveEquipment = 242,
    AbilityView = 243,
    InstantKillStreak = 244,
    LivingPoint = 245,
    ModeAction = 246,
    PetRideCommand = 247,
    PetRideAction = 248,
    Constant = 249,
    HonorPoint = 250,
    StoreGood = 251,
    PetRideEquipment = 252, // EquipmentMate = 252,
    MailAttachment = 253,   // Mail = 5,
    Action = 254,
    Money = 0xFF            // System = 0xFF
}
