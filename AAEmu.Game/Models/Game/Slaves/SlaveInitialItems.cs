namespace AAEmu.Game.Models.Game.Slaves;

public class SlaveInitialItems
{
    public uint SlaveInitialItemPackId { get; set; }
    public byte EquipSlotId { get; set; }
    public uint ItemId { get; set; }
}

public enum SlaveInitialItemPacks : uint
{
    // 1.2
    //EznaCutter = 1,
    //LutesongJunk = 2,
    //MerchantSchooner = 3,
    //HarpoonClipper = 4,
    //AdventureClipper = 5,
    //PinkFishingBoat = 6,
    //WhiteFishingBoat = 7,
    //GreenFishingBoat = 8,
    //PirateShip = 9,
    //DafutaSmallSailingBoat = 10,

    // 3.0
    IzunaSmallSailingBoat = 1,
    PipaSmallSailingBoat = 2,
    TradeShips = 3,
    HarpoonSpeedboat = 4,
    AdventureSpeedboat = 5,
    FishingBoat = 6,
    MediumSailboat = 7,
    PackageProductFishingBoat = 8,
    RoaringSmallSailboat = 9,
    BlankRrecycling = 10,
    MaritimeBattlefieldSmallSailboat = 11,
    MaritimeBattlefieldHarpoonSpeedboat = 12
}
