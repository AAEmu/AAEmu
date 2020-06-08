using System;

namespace AAEmu.Game.Models.Game.Items.Templates
{
    public class EquipItemTemplate : ItemTemplate
    {
        public override Type ClassType => typeof(EquipItem);
        public uint ModSetId { get; set; }
        public uint EquipSetId { get; set; }
        public bool Repairable { get; set; }
        public int DurabilityMultiplier { get; set; }
        public uint RechargeBuffId { get; set; }
        public int ChargeLifetime { get; set; }
        public int ChargeCount { get; set; }
        public ItemLookConvert ItemLookConvert { get; set; }
    }
}
