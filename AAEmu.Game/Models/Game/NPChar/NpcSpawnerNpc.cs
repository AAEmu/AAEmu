using System;
using System.Collections.Generic;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.Units.Route;
using AAEmu.Game.Models.Game.World;
using NLog;

namespace AAEmu.Game.Models.Game.NPChar
{
    public class NpcSpawnerNpc : Spawner<Npc>
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        public uint NpcSpawnerTemplateId { get; set; }
        public uint MemberId { get; set; }
        public string MemberType { get; set; }
        public float Weight { get; set; }

        public NpcSpawnerNpc()
        {
        }

        /// <summary>
        /// Creates a new instance of NpcSpawnerNpcs with a Spawner template id (npc_spanwers)
        /// </summary>
        /// <param name="spawnerTemplateId"></param>
        public NpcSpawnerNpc(uint spawnerTemplateId)
        {
            NpcSpawnerTemplateId = spawnerTemplateId;
        }

        public List<Npc> Spawn(NpcSpawner npcSpawner, uint maxPopulation = 1)
        {
            switch (MemberType)
            {
                case "Npc":
                    return SpawnNpc(npcSpawner, maxPopulation);
                case "NpcGroup":
                    return SpawnNpcGroup(npcSpawner, maxPopulation);
                default:
                    throw new InvalidOperationException($"Tried spawning an unsupported line from NpcSpawnerNpc - Id: {Id}");
            }
        }

        private List<Npc> SpawnNpc(NpcSpawner npcSpawner, uint maxPopulation = 1)
        {
            var npcs = new List<Npc>();
            for (var i = 0; i < maxPopulation; i++)
            {
                var npc = NpcManager.Instance.Create(0, MemberId);
                if (npc == null)
                {
                    _log.Warn($"Npc {MemberId}, from spawn not exist at db");
                    return null;
                }

                _log.Trace($"Spawn npc templateId {MemberId} objId {npc.ObjId} from spawn {Id}, nps spawner Id {NpcSpawnerTemplateId}");

                npc.Transform.ApplyWorldSpawnPosition(npcSpawner.Position);
                if (npc.Transform == null)
                {
                    _log.Error($"Can't spawn npc {MemberId} from spawn {Id}, spawner {NpcSpawnerTemplateId}");
                    return null;
                }

                if (npc.Ai != null)
                {
                    npc.Ai.IdlePosition = npc.Transform.CloneDetached();
                    npc.Ai.GoToSpawn();
                }

                npc.Spawner = new NpcSpawner();
                npc.Spawner.Position = npcSpawner.Position;
                npc.Spawner.Id = npcSpawner.Id;
                npc.Spawner.UnitId = MemberId;
                npc.Spawner.NpcSpawnerIds.Add(NpcSpawnerTemplateId);
                npc.Spawner.Template = npcSpawner.Template;
                npc.Spawner.RespawnTime = (int)Rand.Next(npc.Spawner.Template.SpawnDelayMin, npc.Spawner.Template.SpawnDelayMax);
                npc.Spawn();
                npc.Simulation = new Simulation(npc);
                npcs.Add(npc);
            }

            //_log.Warn($"Spawned Npcs id={MemberId}, maxPopulation={maxPopulation}...");
            
            return npcs;
        }

        private List<Npc> SpawnNpcGroup(NpcSpawner npcSpawner, uint maxPopulation)
        {
            return Spawn(npcSpawner, maxPopulation);
        }
    }
}
