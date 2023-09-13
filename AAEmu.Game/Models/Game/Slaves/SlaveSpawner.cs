using System;
using System.Collections.Generic;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using NLog;

namespace AAEmu.Game.Models.Game.Slaves
{
    public class SlaveSpawner : Spawner<Slave>
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        private List<Slave> _spawned;
        public Slave _lastSpawn;
        private int _scheduledCount;
        private int _spawnCount;
        public uint Count { get; set; } = 1;

        public SlaveSpawner()
        {
            _spawned = new List<Slave>();
            Count = 1;
            _lastSpawn = new Slave();
        }

        public override Slave Spawn(uint objId)
        {
            DoSpawn();
            return _lastSpawn;
        }

        public override void Despawn(Slave slave)
        {
            slave.Delete();
            if (slave.Respawn == DateTime.MinValue)
            {
                _spawned.Remove(slave);
                ObjectIdManager.Instance.ReleaseId(slave.ObjId);
                _spawnCount--;
            }

            if (_lastSpawn == null || _lastSpawn.ObjId == slave.ObjId)
            {
                _lastSpawn = _spawned.Count != 0 ? _spawned[^1] : null;
            }
        }

        public void DecreaseCount(Slave npc)
        {
            _spawnCount--;
            _spawned.Remove(npc);
            if (RespawnTime > 0 && _spawnCount + _scheduledCount < Count)
            {
                npc.Respawn = DateTime.UtcNow.AddSeconds(RespawnTime);
                SpawnManager.Instance.AddRespawn(npc);
                _scheduledCount++;
            }

            npc.Despawn = DateTime.UtcNow.AddSeconds(DespawnTime);
            SpawnManager.Instance.AddDespawn(npc);
        }


        private void DoSpawn()
        {
            var slave = SlaveManager.Instance.Create(this);
            if (slave == null)
            {
                _log.Warn("Slave {0}, from spawn not exist at db", UnitId);
                return;
            }

            slave.Spawner = this;
            //slave.Transform.ApplyWorldSpawnPosition(Position);

            if (slave.Transform.World.IsOrigin())
            {
                _log.Error("Can't spawn slave {1} from spawn {0}", Id, UnitId);
                return;
            }

            _lastSpawn = slave;
        }
    }
}
