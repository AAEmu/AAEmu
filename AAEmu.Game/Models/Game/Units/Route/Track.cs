using System;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.Units.Route
{

    class Track : Patrol
    {
        float distance = 1.5f;
        float MovingDistance = 0.27f;
        public override void Execute(Npc npc)
        {
            Interrupt = false;
            var move = false;
            var x = npc.Position.X - npc.CurrentTarget.Position.X;
            var y = npc.Position.Y - npc.CurrentTarget.Position.Y;
            var z = npc.Position.Z - npc.CurrentTarget.Position.Z;
            var MaxXYZ = Math.Max(Math.Max(Math.Abs(x), Math.Abs(y)), Math.Abs(z));
            float tempMovingDistance;

            if (Math.Abs(x) > distance)
            {
                if (MaxXYZ != Math.Abs(x))
                {
                    tempMovingDistance = Math.Abs(x) / (MaxXYZ / MovingDistance);
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
                move = true;
            }
            if (Math.Abs(y) > distance)
            {
                if (MaxXYZ != Math.Abs(y))
                {
                    tempMovingDistance = Math.Abs(y) / (MaxXYZ / MovingDistance);
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
                move = true;
            }
            if (Math.Abs(z) > distance)
            {
                if (MaxXYZ != Math.Abs(z))
                {
                    tempMovingDistance = Math.Abs(z) / (MaxXYZ / MovingDistance);
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
                move = true;
            }

            if (Math.Max(Math.Max(Math.Abs(x), Math.Abs(y)), Math.Abs(z)) > 20)
            {
                move = false;
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
            var angle = MathUtil.CalculateAngleFrom(npc, npc.CurrentTarget);
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
            else{

                //如果小于差距则停止移动准备攻击
                if (Math.Max(Math.Max(Math.Abs(x), Math.Abs(y)), Math.Abs(z)) <= distance)
                {
                    var combat = new Combat();
                    combat.LastPatrol = LastPatrol;
                    combat.LoopDelay = 2900;
                    combat.Pause(npc);
                    LastPatrol = combat;
                }
                else
                {
                    npc.BroadcastPacket(new SCCombatClearedPacket(npc.CurrentTarget.ObjId), true);
                    npc.BroadcastPacket(new SCCombatClearedPacket(npc.ObjId), true);
                    npc.CurrentTarget = null;
                    npc.StartRegen();
                    npc.BroadcastPacket(new SCTargetChangedPacket(npc.ObjId, 0), true);
                }
                //距离超过指定长度 放弃追踪 停止移动
                moveType.DeltaMovement[1] = 0;
                npc.BroadcastPacket(new SCOneUnitMovementPacket(npc.ObjId, moveType), true);
                Stop(npc);
            }


            if (LastPatrol == null)
            {
                //创建直线巡航回归上次巡航暂停点
                var line = new Line();
                //不可中断，不受外力及攻击影响 类似于处于脱战状态
                line.Interrupt = false;
                line.Loop = false;
                line.Abandon = false;
                line.Pause(npc);
                LastPatrol = line;
            }
        }
    }
}
