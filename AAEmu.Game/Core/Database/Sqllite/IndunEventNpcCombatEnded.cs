using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class IndunEventNpcCombatEnded
{
    public long? Id { get; set; }

    public long? NpcId { get; set; }
}
