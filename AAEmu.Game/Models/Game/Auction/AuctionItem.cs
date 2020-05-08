using System;

namespace AAEmu.Game.Models.Game.Auction
{
    public class AuctionItem
    {
        public ulong ID { get; set; }
        public byte Duration { get; set; }
        public uint ItemID { get; set; }
        public ulong ObjectID { get; set; }
        public byte Grade { get; set; }
        public byte Flags { get; set; }
        public uint StackSize { get; set; }
        public byte DetailType { get; set; }
        public DateTime CreationTime { get; set; }
        public uint LifespanMins { get; set; }
        public uint Type1 { get; set; }
        public byte WorldId { get; set; }
        public DateTime UnsecureDateTime { get; set; }
        public DateTime UnpackDateTIme { get; set; }
        public byte WorldId2 { get; set; }
        public uint Type2 { get; set; }
        public string ClientName { get; set; }
        public uint StartMoney { get; set; }
        public uint DirectMoney { get; set; }
        public ulong TimeLeft { get; set; } //seconds
        public byte BidWorldID { get; set; }
        public uint Type3 { get; set; }
        public string BidderName { get; set; }
        public uint BidMoney { get; set; }
        public uint Extra { get; set; }
    }
}
