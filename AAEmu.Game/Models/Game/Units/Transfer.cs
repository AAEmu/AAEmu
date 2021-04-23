using System;
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
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();

        public override UnitTypeFlag TypeFlag { get; } = UnitTypeFlag.Npc;
        public uint Id { get; set; }
        public uint TemplateId { get; set; }
        public uint BondingObjId { get; set; } = 0;
        public byte AttachPointId { get; set; } = 0;
        public TransferTemplate Template { get; set; }
        public Transfer Bounded { get; set; }
        public TransferSpawner Spawner { get; set; }
        public override UnitCustomModelParams ModelParams { get; set; }
        public List<Doodad> AttachedDoodads { get; set; }
        public DateTime SpawnTime { get; set; }
        public DateTime GameTime { get; set; }
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

        public Transfer()
        {
            AttachedDoodads = new List<Doodad>();
            WorldPos = new WorldPos();
            Position = new Point();
            Routes = new Dictionary<int, List<Point>>();
            TransferPath = new List<Point>();
        }

        public override void AddVisibleObject(Character character)
        {
            //TransferManager.Instance.SpawnAll(character);
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

            for (var i = 0; i < doodadIds.Length; i += 400)
            {
                var offset = i * 400;
                var length = doodadIds.Length - offset;
                var last = length <= 400;
                var temp = new uint[last ? length : 400];
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

        public PacketStream Write(PacketStream stream)
        {
            stream.Write(ObjId);
            stream.Write(TemplateId);
            stream.Write(Name);

            return stream;
        }

        // ******************************************************************************************************************
        // Организуем движение транспорта
        // ******************************************************************************************************************

        public Dictionary<int, List<Point>> Routes { get; set; } // Steering, TransferPath - список всех участков дороги
        public List<Point> TransferPath { get; set; }  // текущий участок дороги
        public bool MoveToPathEnabled { get; set; }       // разрешено движение транспорта
        public bool MoveToForward { get; set; }           // movement direction true -> forward, true -> back
        public int MoveStepIndex { get; set; }            // current checkpoint (where are we running now)
        public float DeltaTime { get; set; } = 0.1f;
        public Vector3 vPosition { get; set; }
        public Vector3 vTarget { get; set; }
        public Vector3 vDistance { get; set; }
        public Vector3 vVelocity { get; set; }
        public float Distance { get; set; }
        public double rad { get; set; }

        public float MaxVelocityForward { get; set; } = 5.4f;
        public float MaxVelocityBackward { get; set; } = 0f;
        public float RangeToCheckPoint { get; set; } = 0.25f; // distance to checkpoint at which it is considered that we have reached it
        public float velAccel { get; set; } = 0.25f;
        public double Angle { get; set; }
        private readonly float angVel;
        private readonly float diffX;
        private readonly float diffY;
        private readonly float diffZ;

        public void GoToPath(Transfer transfer)
        {
            if (transfer == null) { return; }
            if (TransferPath.Count <= 0) { return; }

            transfer.MoveToPathEnabled = !transfer.MoveToPathEnabled;
            transfer.MoveToForward = true;
            transfer.MaxVelocityForward = transfer.Template.PathSmoothing + 1.6f; // попробуем взять эти значения как скорость движения транспорта

            if (!MoveToPathEnabled || transfer.Position == null || !transfer.IsInPatrol)
            {
                //_log.Warn("the route is stopped.");
                StopMove();
                return;
            }

            // presumably the path is already registered in MovePath
            //_log.Warn("trying to get on the road...");
            // first go to the closest checkpoint
            var i = GetMinCheckPoint(transfer, transfer.TransferPath);
            if (i < 0)
            {
                //_log.Warn("no checkpoint found.");
                transfer.StopMove();
                return;
            }

            //_log.Warn("found nearest checkpoint # " + i + " walk there ...");
            //_log.Warn("x:=" + vPosition.X + " y:=" + vPosition.Y + " z:=" + vPosition.Z);
            transfer.MoveToPathEnabled = true;
            transfer.MoveStepIndex = i;
            //_log.Warn("checkpoint #" + i);
            //_log.Warn("x:=" + vPosition.X + " y:=" + vPosition.Y + " z:=" + vPosition.Z);
            var s = TransferPath[transfer.MoveStepIndex];
            transfer.vTarget = new Vector3(s.X, s.Y, s.Z);
            if (transfer.Template.WaitTime > 0)
            {
                var time = transfer.Template.WaitTime;
                WaitTime = DateTime.Now.AddSeconds(time);
                _log.Warn("path #" + Steering);
                _log.Warn("walk to #" + MoveStepIndex);
                _log.Warn("pause to #" + time);
            }


            //if (!(Math.Abs(oldX - vPosition.X) > Tolerance) || !(Math.Abs(oldY - vPosition.Y) > Tolerance) || !(Math.Abs(oldZ - vPosition.Z) > Tolerance))
            //{
            //    return;
            //}

            //oldX = vPosition.X;
            //oldY = vPosition.Y;
            //oldZ = vPosition.Z;
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

        private void StopMove()
        {
            //if (transfer == null) { return; }
            //_log.Warn("stop moving ...");
            Speed = 0;
            RotSpeed = 0;
            Velocity = Vector3.Zero;
            vVelocity = Vector3.Zero;

            var pp = Steering >= Routes.Count - 1 ? Routes[0][0] : Routes[Steering + 1][0];
            var vNewTarget = new Vector3(pp.X, pp.Y, pp.Z);
            rad = MathUtil.CalculateDirection(vPosition, vNewTarget);
            Rot = new Quaternion(0f, 0f, MathUtil.ConvertToDirection(rad), 1f);

            var moveTypeTr = (TransferData)MoveType.GetType(MoveTypeEnum.Transfer);
            moveTypeTr.UseTransferBase(this);
            BroadcastPacket(new SCOneUnitMovementPacket(ObjId, moveTypeTr), true);
            MoveToPathEnabled = false;
        }

        public void MoveTo()
        {
            //if (transfer == null) { return; }

            if (TimeLeft > 0)
            {
                return;
            } // Пауза в начале/конце пути и на остановках

            if (!MoveToPathEnabled || Position == null || !IsInPatrol)
            {
                Throttle = 0;
                StopMove();
                return;
            }

            //vTarget = target;
            vPosition = new Vector3(Position.X, Position.Y, Position.Z);

            // вектор направление на таргет (последовательность аргументов важно, чтобы смотреть на таргет)
            vDistance = vTarget - vPosition; // dx, dy, dz

            // distance to the point where we are moving
            Distance = MathUtil.GetDistance(vTarget, vPosition);

            if (!(Distance > 0))
            {
                // get next path or point # in current path
                OnMove(this);
                return;
            }

            //MaxVelocityForward = 9.0f; // temporarily took a constant

            // accelerate to maximum speed
            Speed += velAccel * DeltaTime;

            //check that it is not more than the maximum forward or reverse speed
            Speed = Math.Clamp(Speed, MaxVelocityBackward, MaxVelocityForward);

            //var velocity = MaxVelocityForward * DeltaTime;
            var velocity = Speed * DeltaTime;
            // вектор направление на таргет (последовательность аргументов важно, чтобы смотреть на таргет)
            // вектор направления необходимо нормализовать
            var direction = vDistance != Vector3.Zero ? Vector3.Normalize(vDistance) : Vector3.Zero;

            // вектор скорости (т.е. координаты, куда попадём двигаясь со скоростью velociti по направдению direction)
            var diff = direction * velocity;
            Position.X += diff.X;
            Position.Y += diff.Y;
            Position.Z += diff.Z;
            var move = Math.Abs(vDistance.X) < RangeToCheckPoint
                       || Math.Abs(vDistance.Y) < RangeToCheckPoint
                       || Math.Abs(vDistance.Z) < RangeToCheckPoint;

            Angle = MathUtil.CalculateDirection(vPosition, vTarget);
            var quat = MathUtil.ConvertRadianToDirectionShort(Angle);
            Rot = new Quaternion(quat.X, quat.Z, quat.Y, quat.W);

            //Velocity = new Vector3(diff.X * 21900, diff.Y * 21900, diff.Z * 21900);
            Velocity = Vector3.Zero;

            AngVel = new Vector3(0f, 0f, (float)Angle); // сюда записывать дельту, в радианах, угла поворота между начальным вектором и конечным
            //if (transfer.TemplateId == 49000)
            //{
            //    // для проверки углов
            //    var v1 = transfer.Position.RotationZ * 0.0078740157;
            //    var v2 = v1 * 3.14159 * 2;
            //    var RotationZdeg = MathUtil.RadianToDegree(v2);
            //    var degree = MathUtil.RadianToDegree(Angle);

            //    //_log.Warn("Angle={0}, _angle={1}, angleTmp={2}, Rot={3}, RotationZ={4}", Angle, _angle, _angleTmp, transfer.Rot, transfer.Position.RotationZ);
            //    _log.Warn("Distance={0}, MoveStepIndex={1}, TransferPath.Count-1={2}", Math.Abs(Distance), MoveStepIndex, TransferPath.Count - 1);
            //    _log.Warn("Angle={0}, degree={1}, transfer.RotationZ={2}, Rot={3}", Angle, degree, quat.Y * 32767, transfer.Rot);
            //    _log.Warn("RotationZ={0}, RotationZdeg={1}", transfer.Position.RotationZ, RotationZdeg);
            //}

            //if (Distance > RangeToCheckPoint)
            if (!move)
            {
                //update class variables
                //transfer.Velocity = vDistance;

                // update TransfersPath variable
                PathPointIndex = MoveStepIndex; // текущая точка, куда движемся
                Steering = Steering; // текущий участок пути

                // moving to the point #
                var moveTypeTr = (TransferData)MoveType.GetType(MoveTypeEnum.Transfer);
                moveTypeTr.UseTransferBase(this);
                SetPosition(moveTypeTr.X, moveTypeTr.Y, moveTypeTr.Z, 0, 0, Helpers.ConvertRadianToSbyteDirection((float)Angle));
                BroadcastPacket(new SCOneUnitMovementPacket(ObjId, moveTypeTr), true);
                //RepeatMove(sim, transfer, targetX, targetY, targetZ);
            }
            else
            {
                // get next path or point # in current path
                OnMove(this);
            }
        }

        private void OnMove(Transfer transfer)
        {
            if (!MoveToPathEnabled)
            {
                //_log.Warn("OnMove disabled");
                StopMove();
                return;
            }

            if (TransferPath.Count <= 0) { return; }

            var s = TransferPath[MoveStepIndex];
            vTarget = new Vector3(s.X, s.Y, s.Z);
            // Проходим по очереди все участки пути из списка, с начала каждого пути
            if (MoveStepIndex >= TransferPath.Count - 1)
            {
                // точки участка пути закончились
                if (MoveStepIndex >= TransferPath.Count - 1 && transfer.Template.TransferAllPaths[Steering].WaitTimeEnd > 0)
                {
                    var time = transfer.Template.TransferAllPaths[Steering].WaitTimeEnd;
                    WaitTime = DateTime.Now.AddSeconds(time);
                    _log.Warn("path #" + Steering);
                    _log.Warn("walk to #" + MoveStepIndex);
                    _log.Warn("pause to #" + time);
                }
                MoveStepIndex = 0;
                if (Steering >= Routes.Count - 1)
                {
                    // закончились все участки пути дороги, нужно начать сначала
                    //_log.Warn("we are at the end point.");
                    Steering = 0; // укажем на начальный путь
                    TransferPath = Routes[Steering];
                    s = TransferPath[MoveStepIndex];
                    //_log.Info("TransfersPath #" + transfer.TemplateId);
                    _log.Warn("next path #" + Steering);
                    _log.Warn("walk to #" + MoveStepIndex);
                    _log.Warn("x:=" + vPosition.X + " y:=" + vPosition.Y + " z:=" + vPosition.Z);
                    vTarget = new Vector3(s.X, s.Y, s.Z);
                    // здесь будет непосредственно пауза между участками дороги, если она есть в базе данных
                    //var time = transfer.Template.TransferPaths[Steering].WaitTimeStart;
                    //RepeatMove(this, transfer, _vPosition.X, _vPosition.Y, _vPosition.Z, time * 1000);
                    // паузу не делаем, так как еще не в начале пути, а в последней точке пути
                    //RepeatMove(this, transfer, vPosition.X, vPosition.Y, vPosition.Z);
                    if (MoveStepIndex == 0 && transfer.Template.TransferAllPaths[Steering].WaitTimeStart > 0)
                    {
                        var time = transfer.Template.TransferAllPaths[Steering].WaitTimeStart;
                        WaitTime = DateTime.Now.AddSeconds(time);
                        _log.Warn("path #" + Steering);
                        _log.Warn("walk to #" + MoveStepIndex);
                        _log.Warn("pause to #" + time);
                    }
                    //if (time > 0)
                    //{
                    //    // здесь будет непосредственно пауза между участками дороги
                    //    //RepeatMove(this, transfer, _vPosition.X, _vPosition.Y, _vPosition.Z, pp, time * 1000);
                    //}
                    //else
                    //{
                    //    // иначе, паузу не делаем
                    //    //RepeatMove(this, transfer, _vPosition.X, _vPosition.Y, _vPosition.Z);
                    //}
                }
                else
                {
                    // продолжим путь
                    Steering++; // укажем на следующий участок пути
                    if (MoveStepIndex >= TransferPath.Count - 1 && transfer.Template.TransferAllPaths[Steering].WaitTimeStart > 0)
                    {
                        var time = transfer.Template.TransferAllPaths[Steering].WaitTimeEnd;
                        WaitTime = DateTime.Now.AddSeconds(time);
                        _log.Warn("path #" + Steering);
                        _log.Warn("walk to #" + MoveStepIndex);
                        _log.Warn("pause to #" + time);
                    }
                    TransferPath = Routes[Steering];
                    s = TransferPath[MoveStepIndex];
                    //_log.Info("TransfersPath #" + transfer.TemplateId);
                    _log.Warn("path #" + Steering);
                    _log.Warn("walk to #" + MoveStepIndex);
                    _log.Warn("x:=" + vPosition.X + " y:=" + vPosition.Y + " z:=" + vPosition.Z);
                    vTarget = new Vector3(s.X, s.Y, s.Z);
                    // здесь будет непосредственно пауза между участками дороги
                    //RepeatMove(this, transfer, vPosition.X, vPosition.Y, vPosition.Z, pp, time * 1000);
                    //if (time > 0)
                    //{
                    //    // здесь будет непосредственно пауза между участками дороги
                    //    //RepeatMove(this, transfer, _vPosition.X, _vPosition.Y, _vPosition.Z, pp, time * 1000);
                    //}
                    //else
                    //{
                    //    // иначе, паузу не делаем
                    //    //RepeatMove(this, transfer, _vPosition.X, _vPosition.Y, _vPosition.Z);
                    //}
                }
            }
            else
            {
                // путь еще не закончился, продолжаем движение
                if (MoveStepIndex == 0 && transfer.Template.TransferAllPaths[Steering].WaitTimeStart > 0)
                {
                    var time = transfer.Template.TransferAllPaths[Steering].WaitTimeStart;
                    WaitTime = DateTime.Now.AddSeconds(time);
                    _log.Warn("path #" + Steering);
                    _log.Warn("walk to #" + MoveStepIndex);
                    _log.Warn("pause to #" + time);
                }
                MoveStepIndex++;
                //_log.Warn("we reached checkpoint go further...");
                //_log.Info("TransfersPath #" + transfer.TemplateId);
                _log.Warn("path #" + Steering);
                _log.Warn("walk to #" + MoveStepIndex);
                _log.Warn("x:=" + vPosition.X + " y:=" + vPosition.Y + " z:=" + vPosition.Z);
                s = TransferPath[MoveStepIndex];
                vTarget = new Vector3(s.X, s.Y, s.Z);
                //if (time > 0)
                //{
                //    // здесь будет пауза в начале участка пути, если она есть в базе данных
                //    //RepeatMove(this, transfer, _vPosition.X, _vPosition.Y, _vPosition.Z, pp, time * 1000);
                //}
                //else
                //{
                //    // иначе, паузу не делаем
                //    //RepeatMove(this, transfer, _vPosition.X, _vPosition.Y, _vPosition.Z);
                //}
            }
        }
    }
}
