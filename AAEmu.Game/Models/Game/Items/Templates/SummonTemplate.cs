using System;

namespace AAEmu.Game.Models.Game.Items.Templates
{
    public class SummonTemplate : ItemTemplate
    {
        public override Type ClassType => typeof(Summon);

        public uint NpcId { get; set; }
    }
}