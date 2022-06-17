using System;
using System.Collections.Generic;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.World;

using NLog;

namespace AAEmu.Game.Models.Game.NPChar.NPSpawner
{
    public class NpcSpawnerNpc : Spawner<Npc>
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        public uint NpcSpawnerId { get; set; }
        public uint MemberId { get; set; }
        public string MemberType { get; set; }
        public float Weight { get; set; }

        public List<Npc> Spawn(NpcSpawner npcSpawner, uint maxPopulation = 1)
        {
            switch (MemberType)
            {
                case "Npc":
                    return new List<Npc> { SpawnNpc(npcSpawner) };
                case "NpcGroup":
                    return SpawnNpcGroup(npcSpawner, maxPopulation);
                default:
                    throw new InvalidOperationException($"Tried spawning an unsupported line from NpcSpawnerNpc - Id: {Id}");
            }
        }

        private Npc SpawnNpc(NpcSpawner npcSpawner)
        {
            var npc = NpcManager.Instance.Create(0, MemberId);
            if (npc == null)
            {
                _log.Warn("Npc {0}, from spawn not exist at db", MemberId);
                return null;
            }

            //_log.Warn("Spawn npc templateId {1} objId {3} from spawn {0}, nps spawner Id {2}", Id, MemberId, NpcSpawnerId, npc.ObjId);

            npc.Transform.ApplyWorldSpawnPosition(npcSpawner.Position);
            if (npc.Transform == null)
            {
                _log.Error("Can't spawn npc {1} from spawn {0}, spawner {2}", Id, MemberId, NpcSpawnerId);
                return null;
            }

            if (npc.Ai != null)
            {
                npc.Ai.IdlePosition = npc.Transform.CloneDetached();
                npc.Ai.GoToSpawn();
            }

            npc.Spawner = npcSpawner;
            //npc.Spawner.Position = npcSpawner.Position;
            npc.Spawner.Id = NpcSpawnerId;
            //npc.Spawner.Template = template;
            npc.Spawner.RespawnTime = (int)Rand.Next(npc.Spawner.Template[NpcSpawnerId].SpawnDelayMin, npc.Spawner.Template[NpcSpawnerId].SpawnDelayMax);
            npc.Spawn();

            return npc;
        }

        private List<Npc> SpawnNpcGroup(NpcSpawner npcSpawner, uint maxPopulation)
        {
            var list = new List<Npc>();
            for (var i = 0; i < maxPopulation; i++)
            {
                list.Add(SpawnNpc(npcSpawner));
            }

            return list;
        }
    }
}
