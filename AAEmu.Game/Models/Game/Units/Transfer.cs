﻿using System;
using System.Collections.Generic;
using System.Numerics;

using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Formulas;
using AAEmu.Game.Models.Game.Transfers;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Utils;

using NLog;

namespace AAEmu.Game.Models.Game.Units
{
    public class Transfer : Unit
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        public override UnitTypeFlag TypeFlag { get; } = UnitTypeFlag.Transfer;
        public uint Id { get; set; }
        public uint TemplateId { get; set; }
        public uint BondingObjId { get; set; } = 0;
        public byte AttachPointId { get; set; } = 0;
        public TransferTemplate Template { get; set; }
        public Transfer Bounded { get; set; }
        public TransferSpawner Spawner { get; set; }
        public override UnitCustomModelParams ModelParams { get; set; }
        public List<Doodad> AttachedDoodads { get; set; }
        public Dictionary<AttachPointKind, Character> AttachedCharacters { get; set; }
        public DateTime SpawnTime { get; set; }
        public float RotationDegrees { get; set; }
        public Quaternion Rot { get; set; } // значение поворота по оси Z должно быть в радианах
        public short RotationX { get; set; }
        public short RotationY { get; set; }
        public short RotationZ { get; set; }
        public Vector3 Velocity { get; set; }
        public short VelX { get; set; }
        public short VelY { get; set; }
        public short VelZ { get; set; }
        public Vector3 AngVel { get; set; }
        public float AngVelX { get; set; }
        public float AngVelY { get; set; }
        public float AngVelZ { get; set; }
        public int Steering { get; set; } = 1;
        public sbyte Throttle { get; set; } // ?
        public int PathPointIndex { get; set; }
        public float Speed { get; set; }
        public float RotSpeed { get; set; }  // ?
        public bool Reverse { get; set; }
        public sbyte RequestSteering { get; set; }
        public sbyte RequestThrottle { get; set; }
        public DateTime WaitTime { get; set; }
        public uint TimeLeft => WaitTime > DateTime.Now ? (uint)(WaitTime - DateTime.Now).TotalMilliseconds : 0;

        public Transfer()
        {
            AttachedDoodads = new List<Doodad>();
            WorldPos = new WorldPos();
            Position = new Point();
            Routes = new Dictionary<int, List<Point>>();
            TransferPath = new List<Point>();
        }

        #region Attributes

        public int Str
        {
            get
            {
                var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Transfer, UnitFormulaKind.Str);
                var parameters = new Dictionary<string, double> { ["level"] = Level };
                var result = formula.Evaluate(parameters);
                var res = (int)result;
                foreach (var bonus in GetBonuses(UnitAttribute.Str))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    {
                        res += (int)(res * bonus.Value / 100f);
                    }
                    else
                    {
                        res += bonus.Value;
                    }
                }

                return res;
            }
        }

        public int Dex
        {
            get
            {
                var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Transfer, UnitFormulaKind.Dex);
                var parameters = new Dictionary<string, double> { ["level"] = Level };
                var res = (int)formula.Evaluate(parameters);
                foreach (var bonus in GetBonuses(UnitAttribute.Dex))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    {
                        res += (int)(res * bonus.Value / 100f);
                    }
                    else
                    {
                        res += bonus.Value;
                    }
                }

                return res;
            }
        }

        public int Sta
        {
            get
            {
                var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Transfer, UnitFormulaKind.Sta);
                var parameters = new Dictionary<string, double> { ["level"] = Level };
                var res = (int)formula.Evaluate(parameters);
                foreach (var bonus in GetBonuses(UnitAttribute.Sta))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    {
                        res += (int)(res * bonus.Value / 100f);
                    }
                    else
                    {
                        res += bonus.Value;
                    }
                }

                return res;
            }
        }

        public int Int
        {
            get
            {
                var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Transfer, UnitFormulaKind.Int);
                var parameters = new Dictionary<string, double> { ["level"] = Level };
                var res = (int)formula.Evaluate(parameters);
                foreach (var bonus in GetBonuses(UnitAttribute.Int))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    {
                        res += (int)(res * bonus.Value / 100f);
                    }
                    else
                    {
                        res += bonus.Value;
                    }
                }

                return res;
            }
        }

        public int Spi
        {
            get
            {
                var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Transfer, UnitFormulaKind.Spi);
                var parameters = new Dictionary<string, double> { ["level"] = Level };
                var res = (int)formula.Evaluate(parameters);
                foreach (var bonus in GetBonuses(UnitAttribute.Spi))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    {
                        res += (int)(res * bonus.Value / 100f);
                    }
                    else
                    {
                        res += bonus.Value;
                    }
                }

                return res;
            }
        }

        public int Fai
        {
            get
            {
                var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Transfer, UnitFormulaKind.Fai);
                var parameters = new Dictionary<string, double> { ["level"] = Level };
                var res = (int)formula.Evaluate(parameters);
                foreach (var bonus in GetBonuses(UnitAttribute.Fai))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    {
                        res += (int)(res * bonus.Value / 100f);
                    }
                    else
                    {
                        res += bonus.Value;
                    }
                }

                return res;
            }
        }

        public override int MaxHp
        {
            get
            {
                var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Transfer, UnitFormulaKind.MaxHealth);
                var parameters = new Dictionary<string, double>
                {
                    ["level"] = Level,
                    ["str"] = Str,
                    ["dex"] = Dex,
                    ["sta"] = Sta,
                    ["int"] = Int,
                    ["spi"] = Spi,
                    ["fai"] = Fai
                };
                var res = (int)formula.Evaluate(parameters);
                foreach (var bonus in GetBonuses(UnitAttribute.MaxHealth))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    {
                        res += (int)(res * bonus.Value / 100f);
                    }
                    else
                    {
                        res += bonus.Value;
                    }
                }

                return res;
            }
        }

        public override int HpRegen
        {
            get
            {
                var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Transfer, UnitFormulaKind.HealthRegen);
                var parameters = new Dictionary<string, double>
                {
                    ["level"] = Level,
                    ["str"] = Str,
                    ["dex"] = Dex,
                    ["sta"] = Sta,
                    ["int"] = Int,
                    ["spi"] = Spi,
                    ["fai"] = Fai
                };
                var res = (int)formula.Evaluate(parameters);
                res += Spi / 10;
                foreach (var bonus in GetBonuses(UnitAttribute.HealthRegen))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    {
                        res += (int)(res * bonus.Value / 100f);
                    }
                    else
                    {
                        res += bonus.Value;
                    }
                }

                return res;
            }
        }

        public override int PersistentHpRegen
        {
            get
            {
                var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Transfer, UnitFormulaKind.PersistentHealthRegen);
                var parameters = new Dictionary<string, double>
                {
                    ["level"] = Level,
                    ["str"] = Str,
                    ["dex"] = Dex,
                    ["sta"] = Sta,
                    ["int"] = Int,
                    ["spi"] = Spi,
                    ["fai"] = Fai
                };
                var res = (int)formula.Evaluate(parameters);
                res /= 5; // TODO ...
                foreach (var bonus in GetBonuses(UnitAttribute.PersistentHealthRegen))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    {
                        res += (int)(res * bonus.Value / 100f);
                    }
                    else
                    {
                        res += bonus.Value;
                    }
                }

                return res;
            }
        }

        public override int MaxMp
        {
            get
            {
                var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Transfer, UnitFormulaKind.MaxMana);
                var parameters = new Dictionary<string, double>
                {
                    ["level"] = Level,
                    ["str"] = Str,
                    ["dex"] = Dex,
                    ["sta"] = Sta,
                    ["int"] = Int,
                    ["spi"] = Spi,
                    ["fai"] = Fai
                };
                var res = (int)formula.Evaluate(parameters);
                foreach (var bonus in GetBonuses(UnitAttribute.MaxMana))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    {
                        res += (int)(res * bonus.Value / 100f);
                    }
                    else
                    {
                        res += bonus.Value;
                    }
                }

                return res;
            }
        }

        public override int MpRegen
        {
            get
            {
                var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Transfer, UnitFormulaKind.ManaRegen);
                var parameters = new Dictionary<string, double>
                {
                    ["level"] = Level,
                    ["str"] = Str,
                    ["dex"] = Dex,
                    ["sta"] = Sta,
                    ["int"] = Int,
                    ["spi"] = Spi,
                    ["fai"] = Fai
                };
                var res = (int)formula.Evaluate(parameters);
                res += Spi / 10;
                foreach (var bonus in GetBonuses(UnitAttribute.ManaRegen))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    {
                        res += (int)(res * bonus.Value / 100f);
                    }
                    else
                    {
                        res += bonus.Value;
                    }
                }

                return res;
            }
        }

        public override int PersistentMpRegen
        {
            get
            {
                var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Transfer, UnitFormulaKind.PersistentManaRegen);
                var parameters = new Dictionary<string, double>
                {
                    ["level"] = Level,
                    ["str"] = Str,
                    ["dex"] = Dex,
                    ["sta"] = Sta,
                    ["int"] = Int,
                    ["spi"] = Spi,
                    ["fai"] = Fai
                };
                var res = (int)formula.Evaluate(parameters);
                res /= 5; // TODO ...
                foreach (var bonus in GetBonuses(UnitAttribute.PersistentManaRegen))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    {
                        res += (int)(res * bonus.Value / 100f);
                    }
                    else
                    {
                        res += bonus.Value;
                    }
                }

                return res;
            }
        }

        #endregion

        public override void Spawn()
        {
            WorldManager.Instance.AddObject(this);
            Show();
        }

        public override void AddVisibleObject(Character character)
        {
            IsVisible = true;
            TransferManager.Instance.Spawn(character, this);
        }

        public override void RemoveVisibleObject(Character character)
        {
            if (character.CurrentTarget != null && character.CurrentTarget == this)
            {
                character.CurrentTarget = null;
                character.SendPacket(new SCTargetChangedPacket(character.ObjId, 0));
            }

            character.SendPacket(Bounded != null
                ? new SCUnitsRemovedPacket(new[] { ObjId, Bounded.ObjId })
                : new SCUnitsRemovedPacket(new[] { ObjId }));

            var doodadIds = new uint[AttachedDoodads.Count];
            for (var i = 0; i < AttachedDoodads.Count; i++)
            {
                doodadIds[i] = AttachedDoodads[i].ObjId;
            }

            for (var i = 0; i < doodadIds.Length; i += SCDoodadsRemovedPacket.MaxCountPerPacket)
            {
                var offset = i * SCDoodadsRemovedPacket.MaxCountPerPacket;
                var length = doodadIds.Length - offset;
                var last = length <= SCDoodadsRemovedPacket.MaxCountPerPacket;
                var temp = new uint[last ? length : SCDoodadsRemovedPacket.MaxCountPerPacket];
                Array.Copy(doodadIds, offset, temp, 0, temp.Length);
                character.SendPacket(new SCDoodadsRemovedPacket(last, temp));
            }
        }

        public override void BroadcastPacket(GamePacket packet, bool self)
        {
            foreach (var character in WorldManager.Instance.GetAround<Character>(this))
            {
                character.SendPacket(packet);
            }
        }

        public PacketStream WriteTelescopeUnit(PacketStream stream)
        {
            stream.WriteBc(ObjId);
            stream.Write(Template.Id);
            stream.WritePosition(Position.X, Position.Y, Position.Z);
            stream.Write(Template.Name);

            return stream;
        }

        // ******************************************************************************************************************
        // Организуем движение транспорта
        // ******************************************************************************************************************

        public Dictionary<int, List<Point>> Routes { get; set; } // Steering, TransferPath - список всех участков дороги
        public List<Point> TransferPath { get; set; }  // текущий участок дороги
        public bool MoveToPathEnabled { get; set; }    // разрешено движение транспорта
        public bool MoveToForward { get; set; }        // movement direction true -> forward, true -> back
        public int MoveStepIndex { get; set; }         // current checkpoint (where are we running now)
        public float DeltaTime { get; set; } = 0.1f;
        public Vector3 vPosition { get; set; }
        public Vector3 vTarget { get; set; }
        public Vector3 vDistance { get; set; }
        public Vector3 vVelocity { get; set; }
        public float Distance { get; set; }
        public float MaxVelocityForward { get; set; } = 5.4f;
        public float MaxVelocityBackward { get; set; } = 0.25f;
        public float RangeToCheckPoint { get; set; } = 0.9f; // distance to checkpoint at which it is considered that we have reached it
        public float velAccel { get; set; } = 0.3f;
        public double Angle { get; set; }

        public void GoToPath(Transfer transfer)
        {
            if (transfer == null) { return; }
            if (transfer.TransferPath.Count <= 0) { return; }

            transfer.MoveToPathEnabled = true;
            transfer.MoveToForward = true;
            transfer.MaxVelocityForward = transfer.Template.PathSmoothing + 1.6f; // попробуем взять эти значения как скорость движения транспорта
            transfer.Speed = 0;
            if (!transfer.MoveToPathEnabled || transfer.Position == null || !transfer.IsInPatrol)
            {
                //_log.Warn("the route is stopped.");
                transfer.StopMove(transfer);
                return;
            }

            // presumably the path is already registered in MovePath
            //_log.Warn("trying to get on the road...");
            // first go to the closest checkpoint
            var i = transfer.GetMinCheckPoint(transfer, transfer.TransferPath);
            if (i < 0)
            {
                //_log.Warn("no checkpoint found.");
                transfer.StopMove(transfer);
                return;
            }

            //_log.Warn("found nearest checkpoint # " + i + " walk there ...");
            //_log.Warn("x:=" + vPosition.X + " y:=" + vPosition.Y + " z:=" + vPosition.Z);
            transfer.MoveToPathEnabled = true;
            transfer.MoveStepIndex = i;
            //_log.Warn("checkpoint #" + i);
            //_log.Warn("x:=" + vPosition.X + " y:=" + vPosition.Y + " z:=" + vPosition.Z);
            var s = transfer.TransferPath[transfer.MoveStepIndex];
            transfer.vTarget = new Vector3(s.X, s.Y, s.Z);
            // 1.начало пути
            if (MoveStepIndex == 0 && Steering == 0 && transfer.Template.WaitTime > 0)
            {
                var time = transfer.Template.WaitTime;
                transfer.WaitTime = DateTime.Now.AddSeconds(time);
                //_log.Info("TransfersPath #" + transfer.Template.Id);
                //_log.Warn("path #" + Steering);
                //_log.Warn("walk to #" + MoveStepIndex);
                //_log.Info("pause to #" + time);
                //_log.Warn("x:=" + transfer.Position.X + " y:=" + transfer.Position.Y + " z:=" + transfer.Position.Z);
            }
        }

        private int GetMinCheckPoint(Transfer transfer, List<Point> pointsList)
        {
            var index = -1;
            // check for a route
            if (pointsList.Count == 0)
            {
                //_log.Warn("no route data.");
                return -1;
            }
            float delta;
            var minDist = 0f;
            transfer.vTarget = new Vector3(transfer.Position.X, transfer.Position.Y, transfer.Position.Z);
            for (var i = 0; i < pointsList.Count; i++)
            {
                transfer.vPosition = new Vector3(pointsList[i].X, pointsList[i].Y, pointsList[i].Z);

                //_log.Warn("#" + i + " x:=" + vPosition.X + " y:=" + vPosition.Y + " z:=" + vPosition.Z);

                delta = MathUtil.GetDistance(transfer.vTarget, transfer.vPosition);
                if (delta > 200) { continue; } // ищем точку не очень далеко от повозки

                if (index == -1) // first assignment
                {
                    index = i;
                    minDist = delta;
                }
                if (delta < minDist) // save if less
                {
                    index = i;
                    minDist = delta;
                }
            }

            return index;
        }

        private void StopMove(Transfer transfer)
        {
            //_log.Warn("stop moving ...");
            transfer.Throttle = 0;
            transfer.Speed = 0;
            transfer.RotSpeed = 0;
            transfer.Velocity = Vector3.Zero;
            transfer.vVelocity = Vector3.Zero;

            //transfer.Angle = MathUtil.CalculateDirection(transfer.vPosition, transfer.vTarget);
            //var quat = Quaternion.CreateFromYawPitchRoll((float)Angle, 0.0f, 0.0f);
            //Rot = new Quaternion(quat.X, quat.Z, quat.Y, quat.W);

            //var moveTypeTr = (TransferData)MoveType.GetType(MoveTypeEnum.Transfer);
            //moveTypeTr.UseTransferBase(transfer);
            //SetPosition(moveTypeTr.X, moveTypeTr.Y, moveTypeTr.Z, 0, 0, Helpers.ConvertRadianToSbyteDirection((float)Angle));
            //BroadcastPacket(new SCOneUnitMovementPacket(ObjId, moveTypeTr), true);
            ////MoveToPathEnabled = false;
        }

        public void MoveTo(Transfer transfer)
        {
            if (transfer.TimeLeft > 0) { return; } // Пауза в начале/конце пути и на остановках

            if (!transfer.MoveToPathEnabled || transfer.Position == null || !transfer.IsInPatrol)
            {
                transfer.StopMove(transfer);
                return;
            }

            transfer.vPosition = new Vector3(transfer.Position.X, transfer.Position.Y, transfer.Position.Z);

            // вектор направление на таргет (последовательность аргументов важно, чтобы смотреть на таргет)
            transfer.vDistance = transfer.vTarget - transfer.vPosition; // dx, dy, dz

            // distance to the point where we are moving
            transfer.Distance = MathUtil.GetDistance(transfer.vTarget, transfer.vPosition);

            if (!(transfer.Distance > 0))
            {
                // get next path or point # in current path
                transfer.NextPoint(transfer);
                return;
            }

            transfer.DoSpeedReduction(transfer);
            // accelerate to maximum speed
            transfer.Speed += transfer.velAccel * transfer.DeltaTime;

            //check that it is not more than the maximum forward or reverse speed
            transfer.Speed = Math.Clamp(transfer.Speed, transfer.MaxVelocityBackward, transfer.MaxVelocityForward);

            //var velocity = MaxVelocityForward * DeltaTime;
            var velocity = transfer.Speed * transfer.DeltaTime;
            // вектор направление на цель (последовательность аргументов важно, чтобы смотреть на цель)
            // вектор направления необходимо нормализовать
            var direction = transfer.vDistance != Vector3.Zero ? Vector3.Normalize(transfer.vDistance) : Vector3.Zero;

            // вектор скорости (т.е. координаты, куда попадём двигаясь со скоростью velociti по направдению direction)
            var diff = direction * velocity;
            transfer.Position.X += diff.X;
            transfer.Position.Y += diff.Y;
            transfer.Position.Z += diff.Z;

            var nextPoint = Math.Abs(transfer.vDistance.X) < transfer.RangeToCheckPoint
                            && Math.Abs(transfer.vDistance.Y) < transfer.RangeToCheckPoint
                            && Math.Abs(transfer.vDistance.Z) < transfer.RangeToCheckPoint;

            transfer.Angle = MathUtil.CalculateDirection(transfer.vPosition, transfer.vTarget);
            if (transfer.Reverse)
            {
                transfer.Angle += MathF.PI;
            }
            var quat = MathUtil.ConvertRadianToDirectionShort(transfer.Angle);
            Rot = new Quaternion(quat.X, quat.Z, quat.Y, quat.W);

            transfer.Velocity = new Vector3(direction.X * 30, direction.Y * 30, direction.Z * 30);
            transfer.AngVel = new Vector3(0f, 0f, (float)transfer.Angle); // сюда записывать дельту, в радианах, угла поворота между начальным вектором и конечным

            //// попробуем двигать прицеп следом за кабиной, если имеется прицеп
            //if (Bounded != null)
            //{
            //    Bounded.Position = Position.Clone();
            //    (Bounded.Position.X, Bounded.Position.Y) = MathUtil.AddDistanceToFront(-9.24417f, Position.X, Position.Y, Position.RotationZ);
            //    Bounded.Rot = Rot;
            //    Bounded.Velocity = Velocity;
            //}

            //if (TemplateId == 700)
            //{
            //    // для проверки

            //    _log.Warn("Reverse=" + Reverse + " Cyclic=" + Template.Cyclic);
            //    _log.Warn("MoveStepIndex=" + MoveStepIndex + " Steering=" + Steering);
            //    _log.Warn("x=" + Position.X + " y=" + Position.Y + " z=" + Position.Z + " Angle=" + Angle + " Rot=" + Rot);
            //    //_log.Warn("velx=" + Velocity.X + " vely=" + Velocity.Y + " velz=" + Velocity.Z);
            //}

            if (nextPoint)
            {
                // get next path or point # in current path
                transfer.NextPoint(transfer);
            }
            else
            {
                // update TransfersPath variable
                //transfer.PathPointIndex = transfer.MoveStepIndex; // текущая точка, куда движемся
                //transfer.Steering = transfer.MoveStepIndex; // текущий участок пути

                // moving to the point #
                var moveTypeTr = (TransferData)MoveType.GetType(MoveTypeEnum.Transfer);
                moveTypeTr.UseTransferBase(this);
                SetPosition(moveTypeTr.X, moveTypeTr.Y, moveTypeTr.Z, 0, 0, Helpers.ConvertRadianToSbyteDirection((float)transfer.Angle));
                BroadcastPacket(new SCOneUnitMovementPacket(ObjId, moveTypeTr), true);

                //if (Bounded == null) { return; }

                //var moveTypeB = (TransferData)MoveType.GetType(MoveTypeEnum.Transfer);
                //moveTypeB.UseTransferBase(Bounded);
                //SetPosition(moveTypeB.X, moveTypeB.Y, moveTypeB.Z, 0, 0, Helpers.ConvertRadianToSbyteDirection((float)Angle));
                //BroadcastPacket(new SCOneUnitMovementPacket(ObjId, moveTypeB), true);
            }
        }

        public void CheckWaitTime(Transfer transfer)
        {
            var time = 0.0d;
            // Проверяем остановки на маршруте

            //// 1.начало пути
            //if (MoveStepIndex == 0 && Steering == 0 && transfer.Template.WaitTime > 0)
            //{
            //    time = transfer.Template.WaitTime;
            //}
            // 2.конец участка
            //else
            if (transfer.MoveStepIndex == 0 && transfer.Steering != 0 && transfer.Template.TransferAllPaths[transfer.Steering - 1].WaitTimeEnd > 0)
            {
                time = transfer.Template.TransferAllPaths[transfer.Steering - 1].WaitTimeEnd;
                WaitTime = DateTime.Now.AddSeconds(time);
            }
            // 3.начало участка
            else if (MoveStepIndex == 0 && transfer.Steering == 0 && transfer.Template.TransferAllPaths[transfer.Steering].WaitTimeStart > 0)
            {
                time = transfer.Template.TransferAllPaths[transfer.Steering].WaitTimeStart;
                WaitTime = DateTime.Now.AddSeconds(time);
            }
            //_log.Info("TransfersPath #" + transfer.Template.Id);
            //_log.Warn("path #" + Steering);
            //_log.Warn("walk to #" + MoveStepIndex);
            //_log.Info("pause to #" + time);
            //_log.Warn("x:=" + transfer.Position.X + " y:=" + transfer.Position.Y + " z:=" + transfer.Position.Z);
        }

        private bool DoSpeedReduction(Transfer transfer)
        {
            var current = transfer.Steering;
            var next = 0;
            if (transfer.Steering + 1 >= transfer.Template.TransferAllPaths.Count)
            {
                next = 0;
            }
            else
            {
                next++;
            }

            if (transfer.Template.TransferAllPaths[current].WaitTimeEnd > 0 || transfer.Template.TransferAllPaths[next].WaitTimeStart > 0)
            {
                // за несколько (3 ?) точек до конца участка будем тормозить
                if (transfer.TransferPath.Count - transfer.MoveStepIndex <= 5)
                {
                    if (transfer.velAccel > 0)
                    {
                        transfer.velAccel *= -1.0f;
                    }
                    return true;
                }
            }
            if (transfer.velAccel < 0)
            {
                transfer.velAccel *= -1.0f;
            }

            return false;
        }

        private void NextPoint(Transfer transfer)
        {
            double time;

            if (!transfer.MoveToPathEnabled)
            {
                //_log.Warn("OnMove disabled");
                transfer.StopMove(transfer);
                return;
            }

            if (transfer.TransferPath.Count <= 0) { return; }

            if (transfer.Template.Cyclic)
            {

                //var s = transfer.TransferPath[transfer.MoveStepIndex];
                //vTarget = new Vector3(s.X, s.Y, s.Z);
                //_log.Info("TransfersPath #" + transfer.Template.Id);
                //_log.Warn("path #" + Steering);
                //_log.Warn("walk to #" + MoveStepIndex);
                //_log.Warn("x:=" + vPosition.X + " y:=" + vPosition.Y + " z:=" + vPosition.Z);
                // Проходим по очереди все участки пути из списка, с начала каждого пути
                if (transfer.MoveStepIndex >= transfer.TransferPath.Count - 1)
                {
                    // точки участка пути закончились
                    transfer.MoveStepIndex = 0;
                    if (transfer.Steering >= transfer.Routes.Count - 1)
                    {
                        // закончились все участки пути дороги, нужно начать сначала
                        //_log.Warn("we are at the end point.");
                        transfer.Steering = 0; // укажем на начальный путь
                        transfer.TransferPath = transfer.Routes[transfer.Steering];
                        var s = transfer.TransferPath[transfer.MoveStepIndex];
                        transfer.vTarget = new Vector3(s.X, s.Y, s.Z);
                    }
                    else
                    {
                        // Проверяем остановки на маршруте
                        //_log.Warn("we reached checkpoint go further...");
                        // продолжим путь
                        transfer.Steering++; // укажем на следующий участок пути
                        transfer.TransferPath = transfer.Routes[transfer.Steering];
                        var s = transfer.TransferPath[transfer.MoveStepIndex];
                        transfer.vTarget = new Vector3(s.X, s.Y, s.Z);
                        // здесь будет пауза в конце участка пути, если она есть в базе данных
                        if (transfer.MoveStepIndex == 0 && transfer.Steering != 0 && (transfer.Template.TransferAllPaths[transfer.Steering - 1].WaitTimeEnd > 0 || transfer.Template.TransferAllPaths[Steering].WaitTimeStart > 0))
                        {
                            time = transfer.Template.TransferAllPaths[transfer.Steering - 1].WaitTimeEnd > 0
                                ? transfer.Template.TransferAllPaths[transfer.Steering - 1].WaitTimeEnd
                                : transfer.Template.TransferAllPaths[transfer.Steering].WaitTimeStart;
                            transfer.WaitTime = DateTime.Now.AddSeconds(time);
                            transfer.StopMove(transfer);
                        }
                    }
                }
                else
                {
                    // здесь будет пауза в начале участка пути, если она есть в базе данных
                    if (transfer.MoveStepIndex == 0 && transfer.Steering == 0 && (transfer.Template.TransferAllPaths[transfer.Steering].WaitTimeStart > 0 || transfer.Template.WaitTime > 0))
                    {
                        time = transfer.Template.TransferAllPaths[transfer.Steering].WaitTimeStart > 0
                            ? transfer.Template.TransferAllPaths[transfer.Steering].WaitTimeStart
                            : transfer.Template.WaitTime;
                        transfer.WaitTime = DateTime.Now.AddSeconds(time);
                        transfer.StopMove(transfer);
                    }

                    // путь еще не закончился, продолжаем движение
                    transfer.MoveStepIndex++;
                    var s = transfer.TransferPath[transfer.MoveStepIndex];
                    transfer.vTarget = new Vector3(s.X, s.Y, s.Z);
                }
            }
            else
            {
                // всего один участок, двигаемся сначала вперед, затем назад
                transfer.Steering = 0; // всегда 0
                //var s = TransferPath[MoveStepIndex];
                // vTarget = new Vector3(s.X, s.Y, s.Z);
                // закончились все участки пути дороги, нужно возвращаться назад
                if (transfer.MoveStepIndex >= transfer.TransferPath.Count - 1)
                {
                    //_log.Warn("we are at the end point.");
                    transfer.Reverse = true;
                    // здесь будет пауза в начале участка пути
                    time = transfer.Template.TransferAllPaths[transfer.Steering].WaitTimeStart > 0
                        ? transfer.Template.TransferAllPaths[transfer.Steering].WaitTimeStart
                        : transfer.Template.WaitTime;
                    transfer.WaitTime = DateTime.Now.AddSeconds(time);
                    transfer.StopMove(transfer);
                }
                // начальная точка, двигаемся вперед
                if (transfer.MoveStepIndex == 0)
                {
                    //_log.Warn("we are at the begin point.");
                    transfer.Reverse = false;
                    // здесь будет пауза в конце участка пути
                    time = transfer.Template.TransferAllPaths[transfer.Steering].WaitTimeEnd > 0
                        ? transfer.Template.TransferAllPaths[transfer.Steering].WaitTimeEnd
                        : transfer.Template.TransferAllPaths[transfer.Steering].WaitTimeStart;
                    transfer.WaitTime = DateTime.Now.AddSeconds(time);
                }

                if (transfer.Reverse)
                {
                    // двигаемся назад по тому же пути
                    transfer.MoveStepIndex--;
                    //TransferPath = Routes[Steering];
                    var s = transfer.TransferPath[transfer.MoveStepIndex];
                    transfer.vTarget = new Vector3(s.X, s.Y, s.Z);
                }
                else
                {
                    // продолжим путь
                    transfer.MoveStepIndex++;
                    //TransferPath = Routes[Steering];
                    var s = transfer.TransferPath[transfer.MoveStepIndex];
                    vTarget = new Vector3(s.X, s.Y, s.Z);
                }
            }
        }
    }
}
