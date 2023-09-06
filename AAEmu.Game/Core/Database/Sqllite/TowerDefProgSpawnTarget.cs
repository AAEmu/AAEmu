using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class TowerDefProgSpawnTarget
{
    public long? Id { get; set; }

    public long? TowerDefProgId { get; set; }

    public long? SpawnTargetId { get; set; }

    public string SpawnTargetType { get; set; }

    public byte[] DespawnOnNextStep { get; set; }
}
