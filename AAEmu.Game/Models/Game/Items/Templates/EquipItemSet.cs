using System.Collections.Generic;

namespace AAEmu.Game.Models.Game.Items.Templates
{
    public class EquipItemSetBonus
    {
        public int NumPieces { get; set; }
        public uint BuffId { get; set; }
        public uint ItemProcId { get; set; }

    }
    public class EquipItemSet
    {
        public uint Id { get; set; }
        public List<EquipItemSetBonus> Bonuses { get; }

        public EquipItemSet()
        {
            Bonuses = new List<EquipItemSetBonus>();
        }
    }
}
