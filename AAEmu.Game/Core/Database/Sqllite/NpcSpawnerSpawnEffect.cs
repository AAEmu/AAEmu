using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class NpcSpawnerSpawnEffect
{
    public long? Id { get; set; }

    public long? SpawnerId { get; set; }

    public double? LifeTime { get; set; }

    public string DespawnOnCreatorDeath { get; set; }

    public string UseSummonerAggroTarget { get; set; }

    public string ActivationState { get; set; }
}
