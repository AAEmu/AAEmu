using System;

namespace AAEmu.Game.Models.Game.Items.Templates
{
    public class EquipItemSetBonusesTemplate
    {
        public uint Id { get; set; }
        public uint SetId { get; set; }
        public uint NumPieces { get; set; }
        public uint BuffId { get; set; }
        public uint ProcId { get; set; }
    }
}
