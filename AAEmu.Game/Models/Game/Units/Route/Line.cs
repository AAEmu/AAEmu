﻿using System;
using System.Collections.Generic;
using System.Text;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.Units.Route
{
    class Line : Patrol
    {
        float distance = 0f;
        float MovingDistance = 0.17f;
        public Point Position { get; set; } 

        public override void Execute(Npc npc)
        {
            if (Position == null)
            {
                Stop(npc);
                return;
            }
            bool move = false;
            float x = npc.Position.X - Position.X;
            float y = npc.Position.Y - Position.Y;
            float z = npc.Position.Z - Position.Z;
            float MaxXYZ = Math.Max(Math.Max(Math.Abs(x), Math.Abs(y)), Math.Abs(z));
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


            //模拟unit / Simulated unit
            var type = (MoveTypeEnum)1;
            //返回moveType对象 / Return moveType object
            var moveType = (UnitMoveType)MoveType.GetType(type);

            //改变NPC坐标 / Changing NPC coordinates
            moveType.X = npc.Position.X;
            moveType.Y = npc.Position.Y;
            moveType.Z = npc.Position.Z;
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
                //广播移动状态 / Broadcasting Mobile State
                npc.BroadcastPacket(new SCOneUnitMovementPacket(npc.ObjId, moveType), true);
                LoopDelay = 500;
                Repet(npc);
            }
            else
            {
                //停止移动 / Stop moving
                moveType.DeltaMovement[1] = 0;
                npc.BroadcastPacket(new SCOneUnitMovementPacket(npc.ObjId, moveType), true);
                LoopAuto(npc);
            }
        }
    }
}
