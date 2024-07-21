using System;

namespace AAEmu.Game.Models.Game.Items.Templates;

public class SummonSlaveTemplate : ItemTemplate
{
    public override Type ClassType => typeof(SummonSlave);

    public uint SlaveId { get; set; }
}
