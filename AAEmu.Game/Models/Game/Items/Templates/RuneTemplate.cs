using System;

namespace AAEmu.Game.Models.Game.Items.Templates
{
    public class RuneTemplate : ItemTemplate
    {
        public override Type ClassType => typeof(Rune);

        public uint EquipSlotGroupId { get; set; }
        public byte EquipLevel { get; set; }
        public byte ItemGradeId { get; set; }
    }
}