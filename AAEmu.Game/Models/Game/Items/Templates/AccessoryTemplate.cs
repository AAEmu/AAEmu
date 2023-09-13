using System;

namespace AAEmu.Game.Models.Game.Items.Templates
{
    public class AccessoryTemplate : EquipItemTemplate
    {
        public override Type ClassType => typeof(Accessory);

        public Wearable WearableTemplate { get; set; }
        public WearableKind KindTemplate { get; set; }
        public WearableSlot SlotTemplate { get; set; }
    }
}