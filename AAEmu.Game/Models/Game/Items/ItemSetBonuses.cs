using System.Collections.Generic;
using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.Game.Models.Game.Items
{
    public class ItemSetBonuses
    {
        public uint EquipItemSetId;
        public Dictionary<uint, EquipItemSetBonusesTemplate> SetBonuses;

        public ItemSetBonuses()
        {
            SetBonuses = new Dictionary<uint, EquipItemSetBonusesTemplate>();
        }
    }
}
