using System;
using System.Collections.Generic;
using System.Numerics;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Tasks.UnitMove;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.Units.Route
{
    /// <summary>
    /// Npc roams around the spawn point in random directions
    /// </summary>
    public class Roaming : Patrol
    {
        public float ReturnDistance;
        public bool InitMovement { get; set; } // инициируем точку возврвата, для возврата, если NPC ушел далеко
        public bool GoBack { get; set; }
        public bool InPatrol { get; set; }
        public Vector3 vPosition { get; set; }
        public Vector3 vPausePosition { get; set; }
        public Vector3 vTarget { get; set; }
        public Vector3 vDistance { get; set; }
        public float RangeToCheckPoint { get; set; } = 0.25f; // distance to checkpoint at which it is considered that we have reached it
        public double Angle { get; set; }
        public Vector3 vBeginPoint { get; set; }
        public Vector3 vEndPoint { get; set; }
        public float DeltaTime { get; set; } = 0.1f;
        public Vector3 vMaxVelocityForwardWalk { get; set; } = new Vector3(1.8f, 1.8f, 1.8f);

        private bool _move;
        private float _velocity;
        private Vector3 _direction;
        private Vector3 _diff;
        private float _newx;
        private float _newy;
        private float _newz;
        private float _x;
        private float _y;
        private float _z;
        private float _mov;
        private sbyte _rotZ;

        /// <summary>
        /// Npc roams around the spawn point in random directions
        /// </summary>
        /// <param name="npc"></param>
        public override void Execute(Npc npc)
        {
            if (npc == null) { return; }

            _move = false;
            _x = npc.Position.X;
            _y = npc.Position.Y;
            _z = npc.Position.Z;
            vPosition = new Vector3(_x, _y, _z); // текущая позиция Npc

            if (!InitMovement)
            {
                // точка возврата
                if (npc.Patrol.PausePosition != null)
                {
                    vPausePosition = new Vector3(npc.Patrol.PausePosition.X, npc.Patrol.PausePosition.Y, npc.Patrol.PausePosition.Z);
                    InitMovement = true;
                    //var time = 30;
                    //TaskManager.Instance.Schedule(new UnitMovePause(this, npc), TimeSpan.FromSeconds(time));
                    //return;
                }
                else
                {
                    vPausePosition = vPosition;
                    npc.Patrol.PausePosition = new Point(_x, _y, _z);
                }

                InitMovement = true;
                //_log.Warn("Template Id={0}, objId={1}, vPausePosition={2}", npc.TemplateId, npc.ObjId, vPausePosition);
            }
            if (!InPatrol)
            {
                _mov = Rand.Next(3, 6);
                _rotZ = (sbyte)Rand.Next(0, 127);
                (_newx, _newy) = MathUtil.AddDistanceToFront(_mov, _x, _y, _rotZ);
                _newz = npc.Position.Z;
                vEndPoint = new Vector3(_newx, _newy, _newz); // точка, куда идём в данный момент
                vBeginPoint = new Vector3(_x, _y, _z); // точка откуда идем
                InPatrol = true;
                //_log.Warn("objId={0}, mov={1}, rotZ={2}, vBeginPoint={3}, vEndPoint={4}", npc.ObjId, _mov, _rotZ, vBeginPoint, vEndPoint);
            }
            if (!GoBack)
            {
                vDistance = vEndPoint - vPosition;
                ReturnDistance = Math.Abs(MathUtil.GetDistance(vPausePosition, vPosition));
                // скорость движения velociti за период времени DeltaTime
                DeltaTime = 0.1f; // temporarily took a constant, later it will be necessary to take the current time
                _velocity = vMaxVelocityForwardWalk.X * DeltaTime;
                // вектор направление на таргет (последовательность аргументов важно, чтобы смотреть на таргет)
                _direction = vEndPoint - vPosition;
                // вектор направления необходимо нормализовать
                if (_direction != Vector3.Zero)
                {
                    _direction = Vector3.Normalize(_direction);
                }

                // вектор скорости (т.е. координаты, куда попадём двигаясь со скоростью velociti по направдению direction)
                _diff = _direction * _velocity;

                npc.Position.X += _diff.X;
                npc.Position.Y += _diff.Y;
                npc.Position.Z += _diff.Z;
                if (Math.Abs(_diff.X) > 0 || Math.Abs(_diff.Y) > 0) { _move = true; }
                if (Math.Abs(vDistance.X) < RangeToCheckPoint) { npc.Position.X = vEndPoint.X; }
                if (Math.Abs(vDistance.Y) < RangeToCheckPoint) { npc.Position.Y = vEndPoint.Y; }
                if (Math.Abs(vDistance.Z) < RangeToCheckPoint) { npc.Position.Z = vEndPoint.Z; }
                if (!(Math.Abs(_diff.X) > 0) && !(Math.Abs(_diff.Y) > 0))
                {
                    if (ReturnDistance > 10.0f)
                    {
                        GoBack = true;
                    }
                    _move = false;
                    InPatrol = false;
                }
                Angle = MathUtil.CalculateAngleFrom(vBeginPoint.X, vBeginPoint.Y, vEndPoint.X, vEndPoint.Y);
                //_log.Warn("objId={0}, mov={1}, rotZ={2}, vBeginPoint={3}, vEndPoint={4}, ReturnDistance={5}, vPosition={6}", npc.ObjId, _mov, _rotZ, vBeginPoint, vEndPoint, ReturnDistance, vPosition);
            }
            else
            {
                vDistance = vPausePosition - vPosition;
                ReturnDistance = Math.Abs(MathUtil.GetDistance(vPausePosition, vPosition));
                // скорость движения velociti за период времени DeltaTime
                DeltaTime = 0.1f; // temporarily took a constant, later it will be necessary to take the current time
                _velocity = vMaxVelocityForwardWalk.X * DeltaTime;
                // вектор направление на таргет (последовательность аргументов важно, чтобы смотреть на таргет)
                _direction = vPausePosition - vPosition;
                // вектор направления необходимо нормализовать
                if (_direction != Vector3.Zero)
                {
                    _direction = Vector3.Normalize(_direction);
                }

                // вектор скорости (т.е. координаты, куда попадём двигаясь со скоростью velociti по направдению direction)
                _diff = _direction * _velocity;

                npc.Position.X += _diff.X;
                npc.Position.Y += _diff.Y;
                npc.Position.Z += _diff.Z;
                if (Math.Abs(_diff.X) > 0 || Math.Abs(_diff.Y) > 0) { _move = true; }
                if (Math.Abs(vDistance.X) < RangeToCheckPoint) { npc.Position.X = vPausePosition.X; }
                if (Math.Abs(vDistance.Y) < RangeToCheckPoint) { npc.Position.Y = vPausePosition.Y; }
                if (Math.Abs(vDistance.Z) < RangeToCheckPoint) { npc.Position.Z = vPausePosition.Z; }
                if (!(Math.Abs(_diff.X) > 0) && !(Math.Abs(_diff.Y) > 0))
                {
                    GoBack = false;
                    _move = false;
                    InPatrol = false;
                }
                Angle = MathUtil.CalculateAngleFrom(vEndPoint.X, vEndPoint.Y, vPausePosition.X, vPausePosition.Y);
                //_log.Warn("Returning: objId ={0}, mov={1}, rotZ={2}, vPausePosition={3}, vEndPoint={4}, ReturnDistance={5}, vPosition={6}", npc.ObjId, _mov, _rotZ, vPausePosition, vEndPoint, ReturnDistance, vPosition);
            }
            // Simulated unit
            var moveType = (UnitMoveType)MoveType.GetType(MoveTypeEnum.Unit);

            moveType.Velocity = new Vector3(_direction.X * 30, _direction.Y * 30, _direction.Z * 30);
            moveType.VelX = (short)moveType.Velocity.X;
            moveType.VelY = (short)moveType.Velocity.Y;
            moveType.VelZ = (short)moveType.Velocity.Z;

            // Change NPC coordinates
            moveType.X = npc.Position.X;
            moveType.Y = npc.Position.Y;
            if (npc.TemplateId == 13677 || npc.TemplateId == 13676) // swimming
            {
                moveType.Z = 98.5993f;
            }
            else if (npc.TemplateId == 13680) // shark
            {
                moveType.Z = 95.5993f;
            }
            else // other
            {
                moveType.Z = AppConfiguration.Instance.HeightMapsEnable ? WorldManager.Instance.GetHeight(npc.Position.ZoneId, npc.Position.X, npc.Position.Y) : npc.Position.Z;
                if (npc.Position.Z - moveType.Z > 2.0)
                {
                    moveType.Z = npc.Position.Z;
                }
            }

            // looks in the direction of movement
            var angle = MathUtil.CalculateAngleFrom(_x, _y, npc.Position.X, npc.Position.Y);
            var rotZ = MathUtil.ConvertDegreeToDirection(angle);
            moveType.RotationX = 0;
            moveType.RotationY = 0;
            moveType.RotationZ = rotZ;
            moveType.DeltaMovement = new sbyte[3];
            moveType.DeltaMovement[0] = 0;
            moveType.DeltaMovement[1] = 127; // 88.. 118
            moveType.DeltaMovement[2] = 0;
            moveType.Flags = 0;
            moveType.ActorFlags = (ushort)ActorMoveType.Walk; // 5-walk, 4-run, 3-stand still
            moveType.Stance = (sbyte)EStance.Idle;            // COMBAT = 0x0, IDLE = 0x1
            moveType.Alertness = (sbyte)AiAlertness.Idle;     // IDLE = 0x0, ALERT = 0x1, COMBAT = 0x2
            moveType.Time = Seq;    // has to change all the time for normal motion.

            // If not far from the spawn point, continue adding tasks or stop moving
            if (_move)
            {
                npc.SetPosition(moveType.X, moveType.Y, moveType.Z, 0, 0, moveType.RotationZ);
                NpcManager.Instance.Movements = new List<(uint, MoveType)>();
                NpcManager.Instance.Movements.Add((npc.ObjId, moveType));
                npc.BroadcastPacket(new SCUnitMovementsPacket(NpcManager.Instance.Movements.ToArray()), true);
                //npc.BroadcastPacket(new SCOneUnitMovementPacket(npc.ObjId, moveType), true);
                Repeat(npc);
            }
            else
            {
                // Stop moving
                moveType.DeltaMovement = new sbyte[3];
                moveType.DeltaMovement[0] = 0;
                moveType.DeltaMovement[1] = 0;
                moveType.DeltaMovement[2] = 0;
                //moveType.DeltaMovement = Vector3.Zero;
                moveType.Velocity = Vector3.Zero;
                npc.SetPosition(moveType.X, moveType.Y, moveType.Z, 0, 0, moveType.RotationZ);
                npc.BroadcastPacket(new SCOneUnitMovementPacket(npc.ObjId, moveType), true);
                // остановиться в вершине на time секунд
                double time = (uint)Rand.Next(5, 15);
                TaskManager.Instance.Schedule(new UnitMovePause(this, npc), TimeSpan.FromSeconds(time));
            }
        }
    }
}
