using System;
using System.Collections.Generic;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
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
        public int _spawnCount;
        public short RotationX;
        public short RotationY;
        public ushort RotationZ;

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
            var transfer = TransferManager.Instance.Create(objId, UnitId, this);
            if (transfer == null)
            {
                _log.Warn("Transfer {0}, from spawn not exist at db", UnitId);
                return null;
            }

            transfer.Spawner = this;
            transfer.Position = Position.Clone();
            transfer.gameTime = DateTime.Now;
            transfer.SpawnTime = DateTime.Now;
            if (transfer.Position == null)
            {
                _log.Error("Can't spawn transfer {1} from spawn {0}", Id, UnitId);
                return null;
            }
            transfer.RotationZ = RotationZ;

            transfer.Spawn();
            _lastSpawn = transfer;
            _spawned.Add(transfer);
            _scheduledCount--;
            _spawnCount++;

            return transfer;
            /*
            // spawn all transports with this TemplateId
            foreach (var tp in transfer.Template.TransferPaths)
            {
                var pathList = TransferManager.Instance.GetTransferPath(tp.PathName);
                if (pathList == null) { continue; }
                // take the original coordinates from the path file
                if (transfer.Position == null)
                {
                    _log.Error("Can't spawn transfer {1} from spawn {0}", Id, UnitId);
                    return null;
                }
                transfer.Position.X = pathList[0].X;
                transfer.Position.Y = pathList[0].Y;
                transfer.Position.Z = pathList[0].Z;
                transfer.Position.WorldId = pathList[0].WorldId;
                transfer.Position.ZoneId = pathList[0].ZoneId;
                if (transfer.Position.X > 0 && transfer.Position.Y > 0)
                {
                    transfer.Spawner.Position = transfer.Position.Clone(); // заменим нулевые координаты координатами из файла пути
                }
                transfer.Spawn();
                _lastSpawn = transfer;
                _spawned.Add(transfer);
                _scheduledCount--;
                _spawnCount++;
            }
            return transfer;
            */
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
    }
}
