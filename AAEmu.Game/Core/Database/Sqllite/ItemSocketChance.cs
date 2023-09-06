using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class ItemSocketChance
{
    public long? Id { get; set; }

    public long? NumSockets { get; set; }

    public long? SuccessRatio { get; set; }
}
