using System;

namespace AAEmu.Game.Models.Game.Items.Templates
{
    public class ItemGradeBuffTemplate
    {
        public virtual Type ClassType => typeof(Item);

        public uint Id { get; set; }
        public uint ItemId { get; set; }
        public uint ItemGradeId { get; set; }
        public uint BuffId { get; set; }
    }
}
