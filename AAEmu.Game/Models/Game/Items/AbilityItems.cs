using System.Collections.Generic;

namespace AAEmu.Game.Models.Game.Items
{
    public class AbilityItems
    {
        public byte Ability { get; set; }
        public EquipItemsTemplate Items { get; set; }
        public List<AbilitySupplyItem> Supplies { get; set; }

        public AbilityItems()
        {
            Supplies = new List<AbilitySupplyItem>();
        }
    }
}