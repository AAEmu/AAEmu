using System;
using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.CashShop
{
    public class CashShopItemDetail : PacketMarshaler
    {
        public int CashShopId { get; set; }
        public int CashUniqId { get; set; }
        public uint ItemTemplateId { get; set; }
        public int ItemCount { get; set; }
        public byte SelectType { get; set; }
        public byte DefaultFlag { get; set; }
        public byte EventType { get; set; }
        public DateTime EventDate { get; set; }
        public byte PriceType { get; set; }
        public int Price { get; set; }
        public int DisPrice { get; set; }
        public int BonusType { get; set; }
        public int BonusCount { get; set; }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(CashShopId);
            stream.Write(CashUniqId);
            stream.Write(ItemTemplateId);
            stream.Write(ItemCount);
            stream.Write(SelectType);
            stream.Write(DefaultFlag);
            stream.Write(EventType);
            stream.Write(EventDate);
            stream.Write(PriceType);
            stream.Write(Price);
            stream.Write(DisPrice);
            stream.Write(BonusType);
            stream.Write(BonusCount);
            return stream;
        }
    }
}
