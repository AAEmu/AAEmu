using System;
using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.CashShop
{
    public class CashShopItem : PacketMarshaler
    {
        public uint CashShopId { get; set; }
        public string CashName { get; set; }
        public byte MainTab { get; set; }
        public byte SubTab { get; set; }
        public byte LevelMin { get; set; }
        public byte LevelMax { get; set; }
        public uint ItemTemplateId { get; set; }
        public byte IsSell { get; set; }
        public byte IsHidden { get; set; }
        public byte LimitType { get; set; }
        public ushort BuyCount { get; set; }
        public byte BuyType { get; set; }
        public uint BuyId { get; set; }
        public DateTime SDate { get; set; }
        public DateTime EDate { get; set; }
        public byte Type { get; set; }
        public uint Price { get; set; }
        public uint Remain { get; set; }
        public int BonusType { get; set; }
        public uint BonusCount { get; set; }
        public byte CmdUi { get; set; }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(CashShopId);
            stream.Write(CashName);
            stream.Write(MainTab);
            stream.Write(SubTab);
            stream.Write(LevelMin);
            stream.Write(LevelMax);
            stream.Write(ItemTemplateId);
            stream.Write(IsSell);
            stream.Write(IsHidden);
            stream.Write(LimitType);
            stream.Write(BuyCount);
            stream.Write(BuyType);
            stream.Write(BuyId);
            stream.Write(SDate);
            stream.Write(EDate);
            stream.Write(Type);
            stream.Write(Price);
            stream.Write(Remain);
            stream.Write(BonusType);
            stream.Write(BonusCount);
            stream.Write(CmdUi);
            return stream;
        }
    }
}
