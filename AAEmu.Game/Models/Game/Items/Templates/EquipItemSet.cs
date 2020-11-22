using System;
using System.Collections.Generic;
using System.Text;
using AAEmu.Game.Models.Game.Items.Procs;
using AAEmu.Game.Models.Game.Skills.Templates;

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
        public List<EquipItemSetBonus> Bonuses {get;}

        public EquipItemSet()
        {
            Bonuses = new List<EquipItemSetBonus>();
        }
    }
}
