using System;
using System.Collections.Generic;

namespace AAEmu.Game.Models.Game.Items.Templates
{
    public class ArmorGradeBuffTemplate
    {
        public virtual Type ClassType => typeof(Item);

        public uint Id { get; set; }
        public uint ArmorTypeId { get; set; }
        public Dictionary<uint, uint> Buffs { get; set; }

        public ArmorGradeBuffTemplate()
        {
            Buffs = new Dictionary<uint, uint>();
        }
    }
}
