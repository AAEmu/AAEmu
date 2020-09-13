using System;
using System.Numerics;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Gimmicks;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.Units.Route
{
    internal class Track : Patrol
    {
        private bool move;
        private float returnDistance;
        private float absoluteReturnDistance;
        private Vector3 newPosition;
        private float diffX;
        private float diffY;
        private float diffZ;

        public override void Execute(Npc npc)
        {
            if (npc == null) { return; } // это не Npc

            if (npc.CurrentTarget == null) { return; } // цели для атаки нет

            Interrupt = false;
            move = false;

            if (!InPatrol)
            {
                vPausePosition = new Vector3(npc.Position.X, npc.Position.Y, npc.Position.Z);
                InPatrol = true;
            }
            Time += DeltaTime;
            Time = Math.Clamp(Time, 0, 1);

            vTarget = new Vector3(npc.CurrentTarget.Position.X, npc.CurrentTarget.Position.Y, npc.CurrentTarget.Position.Z);
            vPosition = new Vector3(npc.Position.X, npc.Position.Y, npc.Position.Z);
            Distance = Math.Abs(MathUtil.GetDistance(vPosition, vTarget));
            returnDistance = Math.Abs(MathUtil.GetDistance(vPausePosition, vPosition));
            absoluteReturnDistance = npc.Template.ReturnDistance > 0f ? npc.Template.ReturnDistance : npc.Template.AbsoluteReturnDistance;

            _log.Warn("Track: Distance {0} returnDistance {1}, AttackStartRangeScale {2}, absoluteReturnDistance {3}, vPausePosition {4}",
                Distance, returnDistance, npc.Template.AttackStartRangeScale, absoluteReturnDistance, vPausePosition);

            if (Distance > npc.Template.AttackStartRangeScale && returnDistance < absoluteReturnDistance)
            {
                newPosition = Vector3.Lerp(vPausePosition, vTarget, Time);
                diffX = newPosition.X - npc.Position.X;
                diffY = newPosition.Y - npc.Position.Y;
                npc.Position.X = newPosition.X;
                npc.Position.Y = newPosition.Y;
                if (Math.Abs(diffX) > 0 || Math.Abs(diffY) > 0) { move = true; }

                //模拟unit
                //Simulated unit
                moveType = (ActorData)UnitMovement.GetType(UnitMovementType.Actor);
                //改变NPC坐标
                //Change NPC coordinates
                moveType.X = newPosition.X;
                moveType.Y = newPosition.Y;
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
                    moveType.Z = AppConfiguration.Instance.HeightMapsEnable
                        ? WorldManager.Instance.GetHeight(npc.Position.ZoneId, npc.Position.X, npc.Position.Y)
                        : npc.Position.Z;
                }

                diffZ = moveType.Z - npc.Position.Z;
                npc.Position.Z = moveType.Z;

                // looks in the direction of movement. Взгляд_NPC_будет(движение_откуда -> движение_куда)
                Angle = MathUtil.CalculateAngleFrom(npc, npc.CurrentTarget);
                var rotZ = MathUtil.ConvertDegreeToDirection(Angle);
                moveType.Rot = new Quaternion(0f, 0f, Helpers.ConvertDirectionToRadian(rotZ), 1f);
                npc.Rot = moveType.Rot;
                moveType.DeltaMovement = new Vector3(0, 1.0f, 0);
                moveType.Flags = 4;     // 5-walk, 4-run, 3-stand still
                moveType.Stance = 0;    // COMBAT = 0x0, IDLE = 0x1
                moveType.Alertness = 2; // IDLE = 0x0, ALERT = 0x1, COMBAT = 0x2
                moveType.Time = Seq;    // has to change all the time for normal motion.

                _log.Warn("Track: vVelocity {0}, vMovingDistance {1}, diffX {2}, diffY {3}", vVelocity, vMovingDistance, diffX, diffY);
                _log.Warn("Track: Angle {0}, rotZ {1}, moveType.DeltaMovement {2}", Angle, rotZ, moveType.DeltaMovement);
            }

            if (move)
            {
                // 广播移动状态
                // Broadcast movement status
                npc.BroadcastPacket(new SCOneUnitMovementPacket(npc.ObjId, moveType), true);
                LoopDelay = 500;
                Repeat(npc);
            }
            else
            {
                // 如果小于差距则停止移动准备攻击
                // Stop moving to prepare for attack if it is smaller than the gap
                if (Distance <= npc.Template.AttackStartRangeScale)
                {
                    var combat = new Combat();
                    combat.LastPatrol = LastPatrol;
                    combat.LoopDelay = 2900;
                    combat.Pause(npc);
                    LastPatrol = combat;
                }
                else
                {
                    if (npc.CurrentTarget is Character chr)
                    {
                        chr.StartRegen();
                    }
                    npc.StartRegen();
                    npc.BroadcastPacket(new SCCombatClearedPacket(npc.CurrentTarget.ObjId), true);
                    npc.BroadcastPacket(new SCCombatClearedPacket(npc.ObjId), true);
                    npc.CurrentTarget = null;
                    npc.BroadcastPacket(new SCTargetChangedPacket(npc.ObjId, 0), true);
                }
                // 距离超过指定长度 放弃追踪 停止移动
                // Distance exceeds the specified length Abandon Tracking Stop moving
                moveType.DeltaMovement = Vector3.Zero;
                npc.Vel = Vector3.Zero;
                npc.BroadcastPacket(new SCOneUnitMovementPacket(npc.ObjId, moveType), true);
                Stop(npc);
            }

            if (LastPatrol != null) { return; }

            // 创建直线巡航回归上次巡航暂停点
            // Create a straight cruise to return to the last cruise pause
            var line = new Line();
            // 不可中断，不受外力及攻击影响 类似于处于脱战状态
            // Uninterruptible, unaffected by external forces and attacks Similar to being in an off-war situation
            line.Interrupt = false;
            line.Loop = false;
            line.Abandon = false;
            line.Pause(npc);
            LastPatrol = line;
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
