using System.Collections.Generic;

namespace AAEmu.Game.Models.Game.NPChar
{
    public enum NpcSpawnerCategory : uint
    {
        Normal = 0,
        Autocreated = 1
    }

    public class NpcSpawnerTemplate
    {
        public uint Id { get; set; }
        public NpcSpawnerCategory NpcSpawnerCategoryId { get; set; }
        public string Name { get; set; }
        public string Comment { get; set; }
        public uint MaxPopulation { get; set; } = 1;
        public float StartTime { get; set; }
        public float EndTime { get; set; }
        public float DestroyTime { get; set; }
        public float SpawnDelayMin { get; set; } = 5;
        public bool ActivationState { get; set; }
        public bool SaveIndun { get; set; }
        public uint MinPopulation { get; set; }
        public float TestRadiusNpc { get; set; } = 3;
        public float TestRadiusPc { get; set; }
        public uint SuspendSpawnCount { get; set; }
        public float SpawnDelayMax { get; set; } = 10;
        public List<NpcSpawnerNpc> Npcs { get; set; }

        public NpcSpawnerTemplate()
        {
        }

        public NpcSpawnerTemplate(uint spawnerId)
        {
            Id = spawnerId;
            var npcs = new List<NpcSpawnerNpc> { new(spawnerId) };
            Npcs = npcs;
        }
    }
}
