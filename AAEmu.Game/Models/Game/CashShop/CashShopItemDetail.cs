using System;
using AAEmu.Commons.Network;
using AAEmu.DB.Game;

namespace AAEmu.Game.Models.Game.CashShop
{
    public class CashShopItemDetail : PacketMarshaler
    {
        public uint CashShopId { get; set; }
        public uint CashUniqId { get; set; }
        public uint ItemTemplateId { get; set; }
        public uint ItemCount { get; set; }
        public byte SelectType { get; set; }
        public byte DefaultFlag { get; set; }
        public byte EventType { get; set; }
        public DateTime EventDate { get; set; }
        public byte PriceType { get; set; }
        public uint Price { get; set; }
        public uint DisPrice { get; set; }
        public uint BonusType { get; set; }
        public uint BonusCount { get; set; }

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

        public static explicit operator CashShopItemDetail(DB.Game.CashShopItem v) =>
            new CashShopItemDetail()
            {
                CashShopId      = v.Id               ,
                CashUniqId      = v.UniqId           ,
                ItemTemplateId  = v.ItemTemplateId   ,
                PriceType       = v.Type             ,
                Price           = v.Price            ,
                ItemCount       = v.ItemCount        ,
                SelectType      = v.SelectType       ,
                DefaultFlag     = v.DefaultFlag      ,
                EventType       = v.EventType        ,
                EventDate       = v.EventDate        ,
                DisPrice        = v.DisPrice         ,
            };
    }
}
