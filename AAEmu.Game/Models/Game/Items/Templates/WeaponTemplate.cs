using System;

namespace AAEmu.Game.Models.Game.Items.Templates
{
    public class WeaponTemplate : EquipItemTemplate
    {
        public override Type ClassType => typeof(Weapon);

        public bool BaseEnchantable { get; set; }
        public Holdable HoldableTemplate { get; set; }
        public bool BaseEquipment { get; set; }
    }
}