using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class DoodadFuncAreaTrigger
{
    public long? Id { get; set; }

    public long? NpcId { get; set; }

    public byte[] IsEnter { get; set; }
}
