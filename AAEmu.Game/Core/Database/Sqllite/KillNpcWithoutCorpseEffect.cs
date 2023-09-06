using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class KillNpcWithoutCorpseEffect
{
    public long? Id { get; set; }

    public long? NpcId { get; set; }

    public double? Radius { get; set; }

    public byte[] GiveExp { get; set; }

    public byte[] Vanish { get; set; }
}
