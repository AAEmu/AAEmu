using System;

namespace AAEmu.Game.Models.Game.Items.Templates
{
    public class SummonSlaveTemplate : ItemTemplate
    {
        public override Type ClassType => typeof(Summon); // TODO - idk if thats the case

        public uint SlaveId { get; set; }
    }
}
