using System.Collections.Generic;

namespace AAEmu.Game.Models.Game.Items.Templates;
public class EquipItemSet
{
    public uint Id { get; set; }
    public List<EquipItemSetBonus> Bonuses { get; }

    public EquipItemSet()
    {
        Bonuses = [];
    }
}
