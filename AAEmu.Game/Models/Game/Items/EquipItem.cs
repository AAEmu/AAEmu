using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.Game.Models.Game.Items
{
    public class EquipItem : Item
    {
        public short Durability { get; set; }
        public uint RuneId { get; set; }

        public virtual int Str => 0;
        public virtual int Dex => 0;
        public virtual int Sta => 0;
        public virtual int Int => 0;
        public virtual int Spi => 0;
        public virtual short MaxDurability => 0;

        public int RepairCost
        {
            get
            {
                var template = (EquipItemTemplate) Template;
                var grade = ItemManager.Instance.GetGradeTemplate(Grade);
                var cost = (ItemManager.Instance.GetDurabilityRepairCostFactor() * 0.0099999998f) *
                           (1f - Durability * 1f / MaxDurability) * template.Price;
                cost = cost * grade.RefundMultiplier * 0.0099999998f;
                cost = (float) Math.Ceiling(cost);
                if (cost < 0 || cost < int.MinValue || cost > int.MaxValue)
                    cost = 0;
                return (int) cost;
            }
        }

        public EquipItem()
        {
        }

        public EquipItem(ulong id, ItemTemplate template, int count) : base(id, template, count)
        {
        }
    }
}
