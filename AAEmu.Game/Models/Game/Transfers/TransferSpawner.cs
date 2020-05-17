using System;
using System.Collections.Generic;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;

using NLog;

namespace AAEmu.Game.Models.Game.Transfers
{
    public class TransferSpawner : Spawner<Transfer>
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        private List<Transfer> _spawned;
        private Transfer _lastSpawn;
        private int _scheduledCount;
        private int _spawnCount;

        public uint Count { get; set; }

        public TransferSpawner()
        {
            _spawned = new List<Transfer>();
            Count = 1;
        }

        public List<Transfer> SpawnAll()
        {
            var list = new List<Transfer>();
            for (var num = _scheduledCount; num < Count; num++)
            {
                var transfer = Spawn(0);
                if (transfer != null)
                {
                    list.Add(transfer);
                }
            }

            return list;
        }

        public override Transfer Spawn(uint objId)
        {
            Transfer transfer = TransferManager.Instance.Create(objId, UnitId);
            if (transfer == null)
            {
                _log.Warn("Transfer {0}, from spawn not exist at db", UnitId);
                return null;
            }

            transfer.Spawner = this;
            transfer.Position = Position.Clone();
            if (transfer.Position == null)
            {
                _log.Error("Can't spawn Transfer {1} from spawn {0}", Id, UnitId);
                return null;
            }

            transfer.Spawn();
            _lastSpawn = transfer;
            _spawned.Add(transfer);
            _scheduledCount--;
            _spawnCount++;

            return transfer;
        }

        public override void Despawn(Transfer transfer)
        {
            transfer.Delete();
            if (transfer.Respawn == DateTime.MinValue)
            {
                _spawned.Remove(transfer);
                ObjectIdManager.Instance.ReleaseId(transfer.ObjId);
                _spawnCount--;
            }

            if (_lastSpawn == null || _lastSpawn.ObjId == transfer.ObjId)
            {
                _lastSpawn = _spawned.Count != 0 ? _spawned[_spawned.Count - 1] : null;
            }
        }

        public void DecreaseCount(Transfer transfer)
        {
            _spawnCount--;
            _spawned.Remove(transfer);
            if (RespawnTime > 0 && (_spawnCount + _scheduledCount) < Count)
            {
                transfer.Respawn = DateTime.Now.AddSeconds(RespawnTime);
                SpawnManager.Instance.AddRespawn(transfer);
                _scheduledCount++;
            }

            transfer.Despawn = DateTime.Now.AddSeconds(DespawnTime);
            SpawnManager.Instance.AddDespawn(transfer);
        }
    }
}
