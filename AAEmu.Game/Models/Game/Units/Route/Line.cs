using System;
using System.Numerics;
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
        public Vector3 Position { get; set; }

        public override void Execute(Npc npc)
        {
            if (Position == default)
            {
                Stop(npc);
                return;
            }
            var move = false;
            var x = npc.Transform.Local.Position.X - Position.X;
            var y = npc.Transform.Local.Position.Y - Position.Y;
            var z = npc.Transform.Local.Position.Z - Position.Z;
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
                    npc.Transform.Local.Translate(tempMovingDistance, 0f, 0f);
                }
                else
                {
                    npc.Transform.Local.Translate(-tempMovingDistance, 0f, 0f);
                }
                if (Math.Abs(x) < tempMovingDistance)
                {
                    npc.Transform.Local.SetPosition(Position.X, npc.Transform.Local.Position.Y, npc.Transform.Local.Position.Z);
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
                    npc.Transform.Local.Translate(0f, tempMovingDistance, 0f);
                }
                else
                {
                    npc.Transform.Local.Translate(0f, -tempMovingDistance, 0f);
                }
                if (Math.Abs(y) < tempMovingDistance)
                {
                    npc.Transform.Local.SetPosition(npc.Transform.Local.Position.X, Position.Y, npc.Transform.Local.Position.Z);
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
                    npc.Transform.Local.Translate(0f, 0f, tempMovingDistance);
                }
                else
                {
                    npc.Transform.Local.Translate(0f, 0f, -tempMovingDistance);
                }
                if (Math.Abs(z) < tempMovingDistance)
                {
                    npc.Transform.Local.SetHeight(Position.Z);
                }
                move = true;
            }

            // 模拟unit
            // simulation unit
            var moveType = (UnitMoveType)MoveType.GetType(MoveTypeEnum.Unit);

            // 改变NPC坐标
            // Change the NPC coordinates
            moveType.X = npc.Transform.Local.Position.X;
            moveType.Y = npc.Transform.Local.Position.Y;
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
                moveType.Z = WorldManager.Instance.GetHeight(npc.Transform);
            }

            // looks in the direction of movement
            var angle = MathUtil.CalculateAngleFrom(npc.Transform.Local.Position.X, npc.Transform.Local.Position.Y, Position.X, Position.Y);
            var rotZ = MathUtil.ConvertDegreeToSByteDirection(angle);
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
