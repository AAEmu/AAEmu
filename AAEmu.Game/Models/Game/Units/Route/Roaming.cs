using System;
using System.Numerics;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Gimmicks;
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
                _mov = Rand.Next(2, 5);
                _rotZ = (sbyte)Rand.Next(0, 127);
                (_newx, _newy) = MathUtil.AddDistanceToFront(_mov, _x, _y, _rotZ);
                //_newz = AppConfiguration.Instance.HeightMapsEnable ? WorldManager.Instance.GetHeight(npc.Position.ZoneId, _newx, _newy) : npc.Position.Z;
                _newz = npc.Position.Z;
                vEndPoint = new Vector3(_newx, _newy, _newz); // точка, куда идём в данный момент
                vBeginPoint = new Vector3(_x, _y, _z); // точка откуда идем
                //Distance = MathUtil.GetDistance(vBeginPoint, vEndPoint);
                InPatrol = true;
                //_log.Warn("objId={0}, mov={1}, rotZ={2}, vBeginPoint={3}, vEndPoint={4}", npc.ObjId, _mov, _rotZ, vBeginPoint, vEndPoint);
            }
            if (!GoBack)
            {
                vDistance = vEndPoint - vPosition;
                ReturnDistance = Math.Abs(MathUtil.GetDistance(vPausePosition, vPosition));
                // скорость движения velociti за период времени DeltaTime
                //DeltaTime = (float)(DateTime.Now - UpdateTime).TotalMilliseconds;
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
                //DeltaTime = (float)(DateTime.Now - UpdateTime).TotalMilliseconds;
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
            moveType = (ActorData)UnitMovement.GetType(UnitMovementType.Actor);
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
                //_newz = AppConfiguration.Instance.HeightMapsEnable ? WorldManager.Instance.GetHeight(npc.Position.ZoneId, npc.Position.X, npc.Position.Y) : npc.Position.Z;
                //moveType.Z = _newz;
                moveType.Z = npc.Position.Z;
            }

            // looks in the direction of movement
            _rotZ = MathUtil.ConvertDegreeToDirection(Angle);
            moveType.Rot = new Quaternion(0f, 0f, Helpers.ConvertDirectionToRadian(_rotZ), 1f);
            npc.Rot = moveType.Rot;

            //moveType.DeltaMovement = new Vector3(0, 127, 0);
            moveType.DeltaMovement = new Vector3(0, 1.0f, 0);
            //moveType.DeltaMovement = new Vector3(diff.X, diff.Y, diff.Z);

            moveType.actorFlags = 5;     // 5-walk, 4-run, 3-stand still
            moveType.Stance = 1;    // COMBAT = 0x0, IDLE = 0x1
            moveType.Alertness = 0; // IDLE = 0x0, ALERT = 0x1, COMBAT = 0x2
            moveType.Time = Seq;    // has to change all the time for normal motion.
            npc.BroadcastPacket(new SCOneUnitMovementPacket(npc.ObjId, moveType), true);

            // If not far from the spawn point, continue adding tasks or stop moving
            if (_move)
            {
                Repeat(npc);
            }
            else
            {
                // Stop moving
                moveType.DeltaMovement = Vector3.Zero;
                moveType.Velocity = Vector3.Zero;
                npc.BroadcastPacket(new SCOneUnitMovementPacket(npc.ObjId, moveType), true);
                // остановиться в вершине на time секунд
                double time = (uint)Rand.Next(5, 15);
                TaskManager.Instance.Schedule(new UnitMovePause(this, npc), TimeSpan.FromSeconds(time));
            }
        }
        public override void Execute(Transfer transfer)
        {
            throw new NotImplementedException();
        }
        public override void Execute(Gimmick gimmick)
        {
            throw new NotImplementedException();
        }
        public override void Execute(BaseUnit unit)
        {
            throw new NotImplementedException();
        }
    }
}
