using System;

namespace AAEmu.Game.Models.Game.Items.Templates
{
    public class SummonMateTemplate : ItemTemplate
    {
        public override Type ClassType => typeof(Summon);

        public uint NpcId { get; set; }
    }
}
