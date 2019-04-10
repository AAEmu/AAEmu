using System;
using System.Collections.Generic;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.World;
using NLog;

namespace AAEmu.Game.Models.Game.NPChar
{
    public class NpcSpawner : Spawner<Npc>
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        private List<Npc> _spawned;
        private Npc _lastSpawn;
        private int _scheduledCount;
        private int _spawnCount;

        public uint Count { get; set; }

        public NpcSpawner()
        {
            _spawned = new List<Npc>();
            Count = 1;
        }

        public List<Npc> SpawnAll()
        {
            var list = new List<Npc>();
            for (var num = _scheduledCount; num < Count; num++)
            {
                var npc = Spawn(0);
                if (npc != null)
                    list.Add(npc);
            }

            return list;
        }

        public override Npc Spawn(uint objId)
        {
            var npc = NpcManager.Instance.Create(objId, UnitId);
            if (npc == null)
            {
                _log.Warn("Npc {0}, from spawn not exist at db", UnitId);
                return null;
            }
            
            npc.Spawner = this;
            npc.Position = Position.Clone();
            if (npc.Position == null)
            {
                _log.Error("Can't spawn npc {1} from spawn {0}", Id, UnitId);
                return null;
            }

            npc.Spawn();
            _lastSpawn = npc;
            _spawned.Add(npc);
            _scheduledCount--;
            _spawnCount++;

            return npc;
        }

        public override void Despawn(Npc npc)
        {
            npc.Delete();
            if (npc.Respawn == DateTime.MinValue)
            {
                _spawned.Remove(npc);
                ObjectIdManager.Instance.ReleaseId(npc.ObjId);
                _spawnCount--;
            }

            if (_lastSpawn == null || _lastSpawn.ObjId == npc.ObjId)
                _lastSpawn = _spawned.Count != 0 ? _spawned[_spawned.Count - 1] : null;
        }

        public void DecreaseCount(Npc npc)
        {
            _spawnCount--;
            _spawned.Remove(npc);
            if (RespawnTime > 0 && (_spawnCount + _scheduledCount) < Count)
            {
                npc.Respawn = DateTime.Now.AddSeconds(RespawnTime);
                SpawnManager.Instance.AddRespawn(npc);
                _scheduledCount++;
            }

            npc.Despawn = DateTime.Now.AddSeconds(DespawnTime);
            SpawnManager.Instance.AddDespawn(npc);
        }
    }
}
