namespace AAEmu.Game.Models.Game.Items;

public enum SlotType : byte
{
    Invalid = 0,
    Equipment = 1,
    Bag = 2,
    Bank = 3,
    Coffer = 4,
    Seized = 5, // связан с почтой
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
    PetRideEquipment = 252,
    MailAttachment = 253,
    Action = 254,
    Money = 0xFF
}
