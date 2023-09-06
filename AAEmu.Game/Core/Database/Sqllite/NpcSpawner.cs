using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class NpcSpawner
{
    public long? Id { get; set; }

    public long? NpcSpawnerCategoryId { get; set; }

    public string Name { get; set; }

    public string Comment { get; set; }

    public long? MaxPopulation { get; set; }

    public double? StartTime { get; set; }

    public double? EndTime { get; set; }

    public double? DestroyTime { get; set; }

    public double? SpawnDelayMin { get; set; }

    public string ActivationState { get; set; }

    public string SaveIndun { get; set; }

    public long? MinPopulation { get; set; }

    public double? TestRadiusNpc { get; set; }

    public double? TestRadiusPc { get; set; }

    public long? SuspendSpawnCount { get; set; }

    public double? SpawnDelayMax { get; set; }
}
