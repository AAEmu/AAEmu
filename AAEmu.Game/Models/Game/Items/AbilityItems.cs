using System.Collections.Generic;

using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Models.Game.Items;

public class AbilityItems
{
    public AbilityType Ability { get; set; }
    public EquipItemsTemplate Items { get; set; }
    public List<AbilitySupplyItem> Supplies { get; set; }

    public AbilityItems()
    {
        Supplies = new List<AbilitySupplyItem>();
    }
}
