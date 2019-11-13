using System;
using System.Collections.Generic;

namespace AAEmu.DB.Game
{
    public partial class CashShopItem
    {
        public uint Id { get; set; }
        public uint UniqId { get; set; }
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
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public byte Type { get; set; }
        public uint Price { get; set; }
        public uint Remain { get; set; }
        public int BonusType { get; set; }
        public uint BounsCount { get; set; }
        public byte CmdUi { get; set; }
        public uint ItemCount { get; set; }
        public byte SelectType { get; set; }
        public byte DefaultFlag { get; set; }
        public byte EventType { get; set; }
        public DateTime EventDate { get; set; }
        public uint DisPrice { get; set; }
    }
}
