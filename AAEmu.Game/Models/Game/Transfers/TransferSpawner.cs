﻿using System;
using System.Collections.Generic;
using System.Numerics;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Units;
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
        private int _spawnCount;
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
            if (transfer.Position == null)
            {
                _log.Error("Can't spawn transfer {1} from spawn {0}", Id, UnitId);
                return null;
            }

            // использование путей из клиента для отдельного потока движения
            // исключаем прицепы
            if (transfer.TemplateId != 46 && transfer.TemplateId != 4 && transfer.TemplateId != 122 && transfer.TemplateId != 135 && transfer.TemplateId != 137)
            {
                if (!transfer.IsInPatrol)
                {
                    // организуем последовательность участков дороги для следования "Транспорта"
                    for (var i = 0; i < transfer.Template.TransferRoads.Count; i++)
                    {
                        transfer.Routes.TryAdd(i, transfer.Template.TransferRoads[i].Pos);
                    }
                    transfer.TransferPath = transfer.Routes[0]; // начнем с самого начала

                    if (transfer.Routes[0] != null)
                    {
                        //_log.Warn("TransfersPath #" + transfer.TemplateId);
                        //_log.Warn("First spawn myX=" + transfer.Position.X + " myY=" + transfer.Position.Y + " myZ=" + transfer.Position.Z + " rotZ=" + transfer.Rot.Z + " rotationZ=" + transfer.Position.RotationZ);
                        transfer.IsInPatrol = true; // so as not to run the route a second time

                        transfer.Steering = 0;
                        transfer.PathPointIndex = 0;

                        // попробуем заспавнить в первой точке пути и попробуем смотреть на следующую точку
                        var point = transfer.Routes[0][0];
                        var point2 = transfer.Routes[0][1];

                        var vPosition = new Vector3(point.X, point.Y, point.Z);
                        var vTarget = new Vector3(point2.X, point2.Y, point2.Z);
                        transfer.Angle = MathUtil.CalculateDirection(vPosition, vTarget);

                        //transfer.Position.RotationZ = MathUtil.ConvertDegreeToDirection(MathUtil.RadianToDegree(transfer.Angle));
                        //var quat = Quaternion.CreateFromYawPitchRoll((float)transfer.Angle, 0.0f, 0.0f);
                        
                        transfer.Position.RotationZ = Helpers.ConvertRadianToSbyteDirection((float)transfer.Angle);
                        var quat = MathUtil.ConvertRadianToDirectionShort(transfer.Angle);

                        transfer.Rot = new Quaternion(quat.X, quat.Z, quat.Y, quat.W);

                        transfer.Position.WorldId = 0;
                        transfer.Position.ZoneId = transfer.Template.TransferRoads[0].ZoneId;
                        transfer.Position.ZoneId = WorldManager.Instance.GetZoneId(transfer.Position.WorldId, point.X, point.Y);
                        transfer.Position.X = point.X;
                        transfer.Position.Y = point.Y;
                        transfer.Position.Z = point.Z;

                        transfer.WorldPos = new WorldPos(Helpers.ConvertLongX(point.X), Helpers.ConvertLongY(point.Y), point.Z);
                        //_log.Warn("TransfersPath #" + transfer.TemplateId);
                        //_log.Warn("New spawn X={0}", transfer.Position.X);
                        //_log.Warn("New spawn Y={0}", transfer.Position.Y);
                        //_log.Warn("New spawn Z={0}", transfer.Position.Z);
                        //_log.Warn("transfer.Rot={0}, rotZ={1}, zoneId={2}", transfer.Rot, transfer.Position.RotationZ, transfer.Position.ZoneId);

                        transfer.GoToPath(transfer);
                        //TransferManager.Instance.AddMoveTransfers(transfer.ObjId, transfer);
                    }
                    else
                    {
                        _log.Warn("PathName: " + transfer.Template.TransferAllPaths[0].PathName + " not found!");
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
