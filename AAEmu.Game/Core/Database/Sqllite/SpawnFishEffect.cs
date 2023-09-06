using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class SpawnFishEffect
{
    public long? Id { get; set; }

    public long? Range { get; set; }

    public long? DoodadId { get; set; }
}
