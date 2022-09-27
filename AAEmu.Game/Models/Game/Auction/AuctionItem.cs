using System;
using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Items;

using Discord;

namespace AAEmu.Game.Models.Game.Auction
{
    public class AuctionItem
    {
        private ulong _timeLeft;

        public ulong Id { get; set; }
        public byte Duration { get; set; }
        public uint ItemId { get; set; }
        public ulong ObjectId { get; set; }
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
        public DateTime UnpackDateTime { get; set; }
        public byte WorldId2 { get; set; }
        public uint ClientId { get; set; }
        public string ClientName { get; set; }
        public uint StartMoney { get; set; }
        public uint DirectMoney { get; set; }
        public ulong TimeLeft
        {
            get { return (ulong)EndTime.Subtract(DateTime.UtcNow).TotalSeconds; } //seconds
            set { _timeLeft = value; }
        }
        public byte BidWorldId { get; set; }
        public uint BidderId { get; set; }
        public string BidderName { get; set; }
        public uint BidMoney { get; set; }
        public uint Extra { get; set; }
        public bool IsDirty { get; set; }

        public void Read(PacketStream stream)
        {
            Id = stream.ReadUInt64();
            Duration = stream.ReadByte();
            ItemId = stream.ReadUInt32();
            ObjectId = stream.ReadUInt64();
            Grade = stream.ReadByte();
            Grade = stream.ReadByte();
            StackSize = stream.ReadUInt32();
            DetailType = stream.ReadByte();
            CreationTime = stream.ReadDateTime();
            LifespanMins = stream.ReadUInt32();
            Type1 = stream.ReadUInt32();
            WorldId = stream.ReadByte();
            UnsecureDateTime = stream.ReadDateTime();
            UnpackDateTime = stream.ReadDateTime();
            WorldId2 = stream.ReadByte();
            ClientId = stream.ReadUInt32();
            ClientName = stream.ReadString();
            StartMoney = stream.ReadUInt32();
            DirectMoney = stream.ReadUInt32();
            TimeLeft = stream.ReadUInt64();
            BidWorldId = stream.ReadByte();
            BidderId = stream.ReadUInt32(); 
            BidderName = stream.ReadString();
            BidMoney = stream.ReadUInt32();
            Extra = stream.ReadUInt32();
        }

        public PacketStream Write(PacketStream stream)
        {
            stream.Write(Id);
            stream.Write(Duration);
            stream.Write(ItemId);
            stream.Write(ObjectId);
            stream.Write(Grade);
            stream.Write((byte)Flags);
            stream.Write(StackSize);
            stream.Write(DetailType);
            stream.Write(DateTime.UtcNow);
            stream.Write(LifespanMins);
            stream.Write(Type1);
            stream.Write(WorldId);
            stream.Write(DateTime.UtcNow);
            stream.Write(DateTime.UtcNow);
            stream.Write(WorldId2);
            stream.Write(ClientId);
            stream.Write(ClientName);
            stream.Write(StartMoney);
            stream.Write(DirectMoney);
            var Random = new Random();
            var offsett = TimeLeft + (ulong)Random.Next(0, 10);
            stream.Write(offsett);
            stream.Write(BidWorldId);
            stream.Write(BidderId);
            stream.Write(BidderName);
            stream.Write(BidMoney);
            stream.Write(Extra);
            return stream;

        }

    }
}
