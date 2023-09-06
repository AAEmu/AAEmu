using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class DoodadFuncRespawn
{
    public long? Id { get; set; }

    public long? MinTime { get; set; }

    public long? MaxTime { get; set; }
}
