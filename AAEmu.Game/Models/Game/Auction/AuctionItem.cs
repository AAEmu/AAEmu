using System;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Auction
{
    public class AuctionItem
    {
        public ulong ID { get; set; }
        public byte Duration { get; set; }
        public uint ItemID { get; set; }
        public ulong ObjectID { get; set; }
        public byte Grade { get; set; }
        public ItemFlag Flags { get; set; }
        public uint StackSize { get; set; }
        public byte DetailType { get; set; }
        public DateTime CreationTime { get; set; }
        public DateTime EndTime { get; set; }
        public uint LifespanMins { get; set; }
        public uint Type1 { get; set; }
        public byte WorldId { get; set; }
        public DateTime UnsecureDateTime { get; set; }
        public DateTime UnpackDateTIme { get; set; }
        public byte WorldId2 { get; set; }
        public uint ClientId { get; set; }
        public string ClientName { get; set; }
        public int StartMoney { get; set; }
        public int DirectMoney { get; set; }
        public ulong TimeLeft { get { return (ulong)EndTime.Subtract(DateTime.UtcNow).TotalSeconds; } } //seconds
        public byte GameServerID { get; set; }
        public uint BidderId { get; set; }
        public string BidderName { get; set; }
        public int BidMoney { get; set; }
        public uint Extra { get; set; }
        public bool IsDirty { get; set; }
    }
}
