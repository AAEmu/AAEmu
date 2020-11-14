using System;
using System.Numerics;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Gimmicks;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.Units.Route
{
    internal class Track : Patrol
    {
        private bool move;
        private float absoluteReturnDistance;
        //private Vector3 newPosition;
        //private float diffX;
        //private float diffY;
        //private float diffZ;
        private float velocity;
        private Vector3 direction;
        private Vector3 diff;
        //private float newx;
        //private float newy;
        private float newz;
        //private float x;
        //private float y;
        //private float z;
        //private float mov;
        private sbyte rotZ;
        private float maxRange;


        public override void Execute(Npc npc)
        {
            if (npc?.CurrentTarget == null) { return; } // цели для атаки нет

            Interrupt = false;
            move = false;

            vPosition = new Vector3(npc.Position.X, npc.Position.Y, npc.Position.Z);

            if (!InPatrol)
            {
                // точка возврата, туда где был NPC в это время
                if (npc.Patrol.LastPatrol != null)
                {
                    vPausePosition = new Vector3(npc.Patrol.LastPatrol.PausePosition.X, npc.Patrol.LastPatrol.PausePosition.Y, npc.Patrol.LastPatrol.PausePosition.Z);
                }
                else
                {
                    vPausePosition = npc.Patrol.PausePosition != null
                        ? new Vector3(npc.Patrol.PausePosition.X, npc.Patrol.PausePosition.Y, npc.Patrol.PausePosition.Z)
                        : vPosition;
                }
                InPatrol = true;

                skill = CheckSkill(npc);
                if (skill == null)
                {
                    maxRange = 3.0f;
                }
                else
                {
                    maxRange = (float)npc.ApplySkillModifiers(skill, SkillAttribute.Range, skill.Template.MaxRange);
                }
            }
            vTarget = new Vector3(npc.CurrentTarget.Position.X, npc.CurrentTarget.Position.Y, npc.CurrentTarget.Position.Z);
            Distance = Math.Abs(MathUtil.GetDistance(vPosition, vTarget));
            ReturnDistance = Math.Abs(MathUtil.GetDistance(vPausePosition, vPosition));
            absoluteReturnDistance = npc.Template.ReturnDistance > 0f ? npc.Template.ReturnDistance : npc.Template.AbsoluteReturnDistance;

            //_log.Warn("Track: Distance {0} returnDistance {1}, AttackStartRangeScale {2}, absoluteReturnDistance {3}, vPausePosition {4}", Distance, ReturnDistance, npc.Template.AttackStartRangeScale, absoluteReturnDistance, vPausePosition);

            if (Distance > maxRange && ReturnDistance < absoluteReturnDistance)
            {
                vDistance = vTarget - vPosition;
                // скорость движения velociti за период времени DeltaTime
                //DeltaTime = (float)(DateTime.Now - UpdateTime).TotalMilliseconds;
                DeltaTime = 0.1f; // temporarily took a constant, later it will be necessary to take the current time
                velocity = vMaxVelocityForwardRun.X * DeltaTime;
                // вектор направление на таргет (последовательность аргументов важно, чтобы смотреть на таргет)
                direction = vTarget - vPosition;
                // вектор направления необходимо нормализовать
                if (direction != Vector3.Zero)
                {
                    direction = Vector3.Normalize(direction);
                }
                // вектор скорости (т.е. координаты, куда попадём двигаясь со скоростью velociti по направдению direction)
                diff = direction * velocity;
                npc.Position.X += diff.X;
                npc.Position.Y += diff.Y;
                npc.Position.Z += diff.Z;
                if (Math.Abs(diff.X) > 0 || Math.Abs(diff.Y) > 0) { move = true; }
                if (Math.Abs(vDistance.X) < RangeToCheckPoint) { npc.Position.X = vTarget.X; }
                if (Math.Abs(vDistance.Y) < RangeToCheckPoint) { npc.Position.Y = vTarget.Y; }
                if (Math.Abs(vDistance.Z) < RangeToCheckPoint) { npc.Position.Z = vTarget.Z; }
                if (!(Math.Abs(diff.X) > 0) && !(Math.Abs(diff.Y) > 0)) { move = false; }
            }

            // 模拟unit
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
                //newz = AppConfiguration.Instance.HeightMapsEnable ? WorldManager.Instance.GetHeight(npc.Position.ZoneId, npc.Position.X, npc.Position.Y) : npc.Position.Z;
                //moveType.Z = newz;
                moveType.Z = npc.Position.Z;
            }

            // looks in the direction of movement
            Angle = MathUtil.CalculateAngleFrom(vPosition.X, vPosition.Y, vTarget.X, vTarget.Y);
            rotZ = MathUtil.ConvertDegreeToDirection(Angle);
            moveType.Rot = new Quaternion(0f, 0f, Helpers.ConvertDirectionToRadian(rotZ), 1f);
            npc.Rot = moveType.Rot;

            moveType.DeltaMovement = new Vector3(0, 1.0f, 0);
            moveType.actorFlags = 4;     // 5-walk, 4-run, 3-stand still
            moveType.Stance = 0;    // COMBAT = 0x0, IDLE = 0x1
            moveType.Alertness = 2; // IDLE = 0x0, ALERT = 0x1, COMBAT = 0x2
            moveType.Time = Seq;    // has to change all the time for normal motion.

            //_log.Warn("Track: vVelocity {0}, vMovingDistance {1}, diffX {2}, diffY {3}", vVelocity, vMovingDistance, diff.X, diff.Y);
            //_log.Warn("Track: Angle {0}, rotZ {1}, moveType.DeltaMovement {2}", Angle, rotZ, moveType.DeltaMovement);

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
                if (Distance <= maxRange)
                {
                    var combat = new Combat();
                    combat.LastPatrol = LastPatrol;
                    combat.LoopDelay = 500; //2900; // задержка перед атакой
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
