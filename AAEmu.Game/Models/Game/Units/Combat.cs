using System;
using System.Numerics;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Gimmicks;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Units.Route;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.Units
{
    internal class Combat : Patrol
    {
        public override void Execute(Npc npc)
        {
            if (npc == null) { return; }

            // If we are killed, the NPC goes to the place of spawn
            var trg = (Unit)npc.CurrentTarget;
            if (trg == null || trg.Hp == 0)
            {
                npc.BroadcastPacket(new SCCombatClearedPacket(npc.ObjId), true);
                npc.BroadcastPacket(new SCTargetChangedPacket(npc.ObjId, 0), true);
                npc.CurrentTarget = null;
                npc.StartRegen();

                // Abandon tracking to stop moving beyond specified length
                Stop(npc);

                // Create Linear Cruise Return to Last Cruise Stop Point
                // Uninterruptible, unaffected by external forces and attacks, similar to being out of combat
                var line = new Line
                {
                    Interrupt = true,
                    Loop = false,
                    Abandon = false
                };
                line.Pause(npc);
                LastPatrol = line;
            }
            else
            {
                // 先判断距离
                // First, estimate the distance
                vPosition = new Vector3(npc.Position.X, npc.Position.Y, npc.Position.Z);
                vTarget = new Vector3(trg.Position.X, trg.Position.Y, trg.Position.Z);
                //vDistance = vPosition - vTarget; // dx, dy, dz
                Distance = MathUtil.GetDistance(vPosition, vTarget);

                // 如果最大值超过distance 则放弃攻击转而进行追踪
                // If the maximum value exceeds distance, the attack is abandoned and the tracking is followed.
                float maxRange;
                skill = CheckSkill(npc);
                if (skill == null)
                {
                    skill = new Skill(SkillManager.Instance.GetSkillTemplate((uint)npc.Template.BaseSkillId));
                    maxRange = 3.0f;
                }
                else
                {
                    maxRange = (float)npc.ApplySkillModifiers(skill, SkillAttribute.Range, skill.Template.MaxRange);
                }

                if (Distance > maxRange)
                {
                    var track = new Track();
                    track.Pause(npc);
                    track.LastPatrol = LastPatrol;
                    LastPatrol = track;
                    Stop(npc);
                }
                else
                {
                    CheckBuff(npc); // кастуем бафф, если есть

                    skill = CheckSkill(npc) ?? new Skill(SkillManager.Instance.GetSkillTemplate((uint)npc.Template.BaseSkillId)); // проверим, есть ли скилл для использования

                    var casterType = SkillCaster.GetByType(EffectOriginType.Skill); // who uses
                    casterType.ObjId = npc.ObjId;

                    var targetType = GetSkillCastTarget(npc, skill);

                    var flag = 0;
                    var flagType = flag & 15;
                    var skillObject = SkillObject.GetByType((SkillObjectType)flagType);

                    skill.Use(npc, casterType, targetType, skillObject);

                    if (skill.Template.CastingTime != 0)
                    {
                        LoopDelay = skill.Template.CastingTime;
                    }
                    else if (skill.Template.CooldownTime != 0)
                    {
                        LoopDelay = skill.Template.CooldownTime;
                    }
                    else
                    {
                        LoopDelay = 3000;
                    }

                    LoopAuto(npc);
                    //}
                }
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
