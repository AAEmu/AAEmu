using System;
using System.Collections.Generic;
using System.Text;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units.Movements;

namespace AAEmu.Game.Models.Game.Units.Route
{
   
    class Track : Patrol
    {
        float distance = 1.5f;
        float MovingDistance = 0.17f;
        public override void Execute(Npc npc)
        {
            bool move = true;
            float x = npc.Position.X - npc.CurrentTarget.Position.X;
            float y = npc.Position.Y - npc.CurrentTarget.Position.Y;
            float z = npc.Position.Z - npc.CurrentTarget.Position.Z;

            if (Math.Abs(x) > distance) { 
                if (x < 0)
                {
                    npc.Position.X += MovingDistance;
                }
                else
                {
                    npc.Position.X -= MovingDistance;
                }
                move = true;
            }
            if (Math.Abs(y) > distance)
            {
                if (y < 0)
                {
                    npc.Position.Y += MovingDistance;
                }
                else
                {
                    npc.Position.Y -= MovingDistance;
                }
                move = true;
            }
            if (Math.Abs(z) > distance)
            {
                if (z < 0)
                {
                    npc.Position.Z += MovingDistance;
                }
                else
                {
                    npc.Position.Z -= MovingDistance;
                }
                move = true;
            }

            if (Math.Max(Math.Max(Math.Abs(x), Math.Abs(y)), Math.Abs(z)) > 20)
            {
                move = false;
                Stop(npc);
            }

            //模拟unit
            var type = (MoveTypeEnum)1;
            //返回moveType对象
            var moveType = (UnitMoveType)MoveType.GetType(type);

            //改变NPC坐标
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
                //广播移动状态
                npc.BroadcastPacket(new SCOneUnitMovementPacket(npc.ObjId, moveType), true);
                LoopDelay = 500;
                Repet(npc);
            }
            else{
                //停止移动
                moveType.DeltaMovement[1] = 0;
                npc.BroadcastPacket(new SCOneUnitMovementPacket(npc.ObjId, moveType), true);
                LoopAuto(npc);
            }
        }
    }
}
