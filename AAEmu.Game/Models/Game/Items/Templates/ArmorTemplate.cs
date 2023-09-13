using System;

namespace AAEmu.Game.Models.Game.Items.Templates
{
    public class ArmorTemplate : EquipItemTemplate
    {
        public override Type ClassType => typeof(Armor);

        public Wearable WearableTemplate { get; set; }
        public WearableKind KindTemplate { get; set; }
        public WearableSlot SlotTemplate { get; set; }
        public bool BaseEnchantable { get; set; }
        public bool BaseEquipment { get; set; }
    }
}