using System;
using System.Collections.Generic;
using System.Numerics;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Route;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Utils;
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
        public float RotationX;
        public float RotationY;
        public float RotationZ;

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
            transfer.GameTime = DateTime.Now;
            transfer.SpawnTime = DateTime.Now;
            if (transfer.Position == null)
            {
                _log.Error("Can't spawn transfer {1} from spawn {0}", Id, UnitId);
                return null;
            }

            if (transfer.TemplateId != 46 && transfer.TemplateId != 4 && transfer.TemplateId != 122)
            {
                if (!transfer.IsInPatrol)
                {
                    var path = new Simulation(transfer, transfer.Template.PathSmoothing);
                    // организуем последовательность "Дорог" для следования "Транспорта", два(0,1) в одну сторону и два(2,3) в обратную
                    for (var i = 0; i < transfer.Template.TransferRoads.Count; i++)
                    {
                        path.Routes.TryAdd(i, transfer.Template.TransferRoads[i].Pos);
                    }
                    path.LoadTransferPathFromRoutes(0); // начнем с самого начала

                    if (path.Routes[0] != null)
                    {
                        _log.Warn("Transfer #" + transfer.TemplateId);
                        _log.Warn("First spawn myX=" + transfer.Position.X + " myY=" + transfer.Position.Y + " myZ=" + transfer.Position.Z + " rotZ=" + transfer.Rot.Z + " rotationZ=" + transfer.Position.RotationZ);
                        transfer.IsInPatrol = true; // so as not to run the route a second time
                        
                        transfer.Steering = 0;
                        transfer.PathPointIndex = 0;

                        // попробуем заспавнить в последней точке пути (она как раз напротив стоянки)
                        // попробуем смотреть на следующую точку

                        var point = path.Routes[path.Routes.Count - 1][path.Routes[path.Routes.Count - 1].Count - 1];
                        var point2 = path.Routes[0][0];

                        var vPosition = new Vector3(point.X, point.Y, point.Z);
                        var vTarget = new Vector3(point2.X, point2.Y, point2.Z);
                        path.Angle = MathUtil.CalculateDirection(vPosition, vTarget);
                        transfer.Position.RotationZ = MathUtil.ConvertDegreeToDirection(MathUtil.RadianToDegree(path.Angle));
                        transfer.Rot = new Quaternion(0f, 0f, MathUtil.ConvertToDirection(path.Angle), 1f);

                        transfer.Position.WorldId = 1;
                        transfer.Position.ZoneId = WorldManager.Instance.GetZoneId(transfer.Position.WorldId, point.X, point.Y);
                        transfer.Position.X = point.X;
                        transfer.Position.Y = point.Y;
                        transfer.Position.Z = point.Z;

                        transfer.WorldPos = new WorldPos(Helpers.ConvertLongY(point.X), Helpers.ConvertLongY(point.Y), point.Z);
                        _log.Warn("Transfer #" + transfer.TemplateId);
                        _log.Warn("New spawn myX={0}, myY={1}, myZ={2}, rotZ={3}, zoneId={4}", transfer.Position.X, transfer.Position.Y, transfer.Position.Z, transfer.Position.RotationZ, transfer.Position.ZoneId);

                        path.InPatrol = false;

                        path.GoToPath(transfer, true);
                    }
                    else
                    {
                        _log.Warn("PathName: " + transfer.Template.TransferPaths[0].PathName + " not found!");
                    }
                }
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
    }
}
