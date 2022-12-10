namespace AAEmu.Game.Models.Game.Slaves
{
    public class SlaveInitialItems
    {
        public uint slaveInitialItemPackId { get; set; }
        public byte equipSlotId { get; set; }
        public uint itemId { get; set; }
    }

    public enum SlaveInitialItemPacks : uint
    {
        EznaCutter = 1,
        LutesongJunk = 2,
        MerchantSchooner = 3,
        HarpoonClipper = 4,
        AdventureClipper = 5,
        PinkFishingBoat = 6,
        WhiteFishingBoat = 7,
        GreenFishingBoat = 8,
        PirateShip = 9,
        DafutaSmallSailingBoat = 10 
    }
}
