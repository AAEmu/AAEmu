using System;
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
                if (Math.Abs(MaxXYZ - Math.Abs(x)) > tolerance)
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
                if (Math.Abs(MaxXYZ - Math.Abs(y)) > tolerance)
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
                if (Math.Abs(MaxXYZ - Math.Abs(z)) > tolerance)
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
                if (Math.Abs(z) < tempMovingDistance)
                {
                    npc.Position.Z = Position.Z;
                }
                move = true;
            }

            // 模拟unit
            // simulation unit
            var moveType = (UnitMoveType)MoveType.GetType(MoveTypeEnum.Unit);

            // 改变NPC坐标
            // Change the NPC coordinates
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
            }

            // looks in the direction of movement
            var angle = MathUtil.CalculateAngleFrom(npc.Position.X, npc.Position.Y, Position.X, Position.Y);
            var rotZ = MathUtil.ConvertDegreeToDirection(angle);
            moveType.RotationX = 0;
            moveType.RotationY = 0;
            moveType.RotationZ = rotZ;

            moveType.ActorFlags = 5;     // 5-walk, 4-run, 3-stand still
            moveType.DeltaMovement = new sbyte[3];
            moveType.DeltaMovement[0] = 0;
            moveType.DeltaMovement[1] = 127;
            moveType.DeltaMovement[2] = 0;
            moveType.Stance = 1;    // COMBAT = 0x0, IDLE = 0x1
            moveType.Alertness = 0; // IDLE = 0x0, ALERT = 0x1, COMBAT = 0x2
            moveType.Time += 50;    // has to change all the time for normal motion.

            if (move)
            {
                // 广播移动状态
                // broadcast mobile status
                npc.BroadcastPacket(new SCOneUnitMovementPacket(npc.ObjId, moveType), true);
                LoopDelay = 500;
                Repeat(npc);
            }
            else
            {
                // 停止移动
                // stop moving
                moveType.DeltaMovement[1] = 0;
                npc.BroadcastPacket(new SCOneUnitMovementPacket(npc.ObjId, moveType), true);
                LoopAuto(npc);
            }
        }
    }
}
