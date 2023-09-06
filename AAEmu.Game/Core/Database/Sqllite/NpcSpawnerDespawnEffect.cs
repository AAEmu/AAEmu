using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class NpcSpawnerDespawnEffect
{
    public long? Id { get; set; }

    public long? SpawnerId { get; set; }
}
