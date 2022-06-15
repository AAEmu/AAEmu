using System;
using System.Collections.Generic;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.World.Transform;

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

        public List<Npc> Spawn(WorldSpawnPosition position, NpcSpawnerTemplate template)
        {
            switch (MemberType)
            {
                case "Npc":
                    return SpawnNpc(position, template);
                case "NpcGroup":
                    return SpawnNpcGroup(position, template);
                default:
                    throw new InvalidOperationException($"Tried spawning an unsupported line from NpcSpawnerNpc - Id: {Id}");
            }
        }

        private List<Npc> SpawnNpc(WorldSpawnPosition position, NpcSpawnerTemplate template)
        {
            var npc = NpcManager.Instance.Create(0, MemberId);
            if (npc == null)
            {
                _log.Warn("Npc {0}, from spawn not exist at db", MemberId);
                return null;
            }

            _log.Warn("Spawn npc templateId {1} objId {3} from spawn {0}, nps spawner Id {2}", Id, MemberId, NpcSpawnerId, npc.ObjId);

            npc.Transform.ApplyWorldSpawnPosition(position);
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

            npc.Spawner = new NpcSpawner();
            npc.Spawner.Position = position;
            npc.Spawner.Id = NpcSpawnerId;
            npc.Spawner.Template = template;
            npc.Spawner.RespawnTime = (int) Rand.Next(template.SpawnDelayMin, template.SpawnDelayMax);
            npc.Spawn();

            return new List<Npc> { npc };
        }

        private List<Npc> SpawnNpcGroup(WorldSpawnPosition position, NpcSpawnerTemplate template)
        {
            // TODO: Get list of NPCs in group
            // Loop over list, call SpawnNpc
            return SpawnNpc(position, template);
        }
    }
}
