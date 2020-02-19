using System;
using System.Collections.Generic;
using System.Text;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.Units.Route
{
    class Line : Patrol
    {
        float distance = 0f;
        float MovingDistance = 0.27f;
        public Point Position { get; set; } 

        public override void Execute(Npc npc)
        {
            if (Position == null)
            {
                Stop(npc);
                return;
            }
            var move = false;
            var x = npc.Position.X - Position.X;
            var y = npc.Position.Y - Position.Y;
            var z = npc.Position.Z - Position.Z;
            var MaxXYZ = Math.Max(Math.Max(Math.Abs(x), Math.Abs(y)), Math.Abs(z));
            float tempMovingDistance;

            if (Math.Abs(x) > distance)
            {
                if (MaxXYZ != Math.Abs(x))
                {
                    tempMovingDistance = Math.Abs(x) / (MaxXYZ / MovingDistance);
                    tempMovingDistance = Math.Min(tempMovingDistance, MovingDistance);
                }
                else
                {
                    tempMovingDistance = MovingDistance;
                }

                if (x < 0)
                {
                    npc.Position.X += tempMovingDistance;
                }
                else
                {
                    npc.Position.X -= tempMovingDistance;
                }
                if (Math.Abs(x) < tempMovingDistance)
                {
                    npc.Position.X = Position.X;
                }
                move = true;
            }
            if (Math.Abs(y) > distance)
            {
                if (MaxXYZ != Math.Abs(y))
                {
                    tempMovingDistance = Math.Abs(y) / (MaxXYZ / MovingDistance);
                    tempMovingDistance = Math.Min(tempMovingDistance, MovingDistance);
                }
                else
                {
                    tempMovingDistance = MovingDistance;
                }
                if (y < 0)
                {
                    npc.Position.Y += tempMovingDistance;
                }
                else
                {
                    npc.Position.Y -= tempMovingDistance;
                }
                if (Math.Abs(y) < tempMovingDistance)
                {
                    npc.Position.Y = Position.Y;
                }
                move = true;
            }
            if (Math.Abs(z) > distance)
            {
                if (MaxXYZ != Math.Abs(z))
                {
                    tempMovingDistance = Math.Abs(z) / (MaxXYZ / MovingDistance);
                    tempMovingDistance = Math.Min(tempMovingDistance, MovingDistance);
                }
                else
                {
                    tempMovingDistance = MovingDistance;
                }
                if (z < 0)
                {
                    npc.Position.Z += tempMovingDistance;
                }
                else
                {
                    npc.Position.Z -= tempMovingDistance;
                }
                if(Math.Abs(z)< tempMovingDistance)
                {
                    npc.Position.Z = Position.Z;
                }
                move = true;
            }


            //模拟unit
            var type = (MoveTypeEnum)1;
            //返回moveType对象
            var moveType = (UnitMoveType)MoveType.GetType(type);

            //改变NPC坐标
            moveType.X = npc.Position.X;
            moveType.Y = npc.Position.Y;
            moveType.Z = AppConfiguration.Instance.HeightMapsEnable
                ? WorldManager.Instance.GetHeight(npc.Position.ZoneId, npc.Position.X, npc.Position.Y)
                : npc.Position.Z;
            // looks in the direction of movement
            var angle = MathUtil.CalculateAngleFrom(npc.Position.X, npc.Position.Y, Position.X, Position.Y);
            var rotZ = MathUtil.ConvertDegreeToDirection(angle);
            moveType.RotationX = 0;
            moveType.RotationY = 0;
            moveType.RotationZ = rotZ;

            moveType.Flags = 5;
            moveType.DeltaMovement = new sbyte[3];
            moveType.DeltaMovement[0] = 0;
            moveType.DeltaMovement[1] = 127;
            moveType.DeltaMovement[2] = 0;
            moveType.Stance = 0;
            moveType.Alertness = 2;
            moveType.Time = Seq;


            if (move)
            {
                //广播移动状态
                npc.BroadcastPacket(new SCOneUnitMovementPacket(npc.ObjId, moveType), true);
                LoopDelay = 500;
                Repet(npc);
            }
            else
            {
                //停止移动
                moveType.DeltaMovement[1] = 0;
                npc.BroadcastPacket(new SCOneUnitMovementPacket(npc.ObjId, moveType), true);
                LoopAuto(npc);
            }
        }
    }
}
