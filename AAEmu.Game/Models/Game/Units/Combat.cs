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
                    Interrupt = false,
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
                vDistance = vPosition - vTarget; // dx, dy, dz
                Distance = MathUtil.GetDistance(vPosition, vTarget);

                // 如果最大值超过distance 则放弃攻击转而进行追踪
                // If the maximum value exceeds distance, the attack is abandoned and the tracking is followed.
                if (Distance > npc.Template.AttackStartRangeScale)
                {
                    var track = new Track();
                    track.Pause(npc);
                    track.LastPatrol = LastPatrol;
                    LastPatrol = track;
                    Stop(npc);
                }
                else
                {
                    LoopDelay = 2000; //npc.Template.BaseSkillDelay;
                    var skillId = (uint)npc.Template.BaseSkillId;
                    var skillCaster = SkillCaster.GetByType(EffectOriginType.Skill); // who uses
                    skillCaster.ObjId = npc.ObjId;
                    var skillCastTarget = SkillCastTarget.GetByType(SkillCastTargetType.Unit); // who is being used
                    skillCastTarget.ObjId = npc.CurrentTarget.ObjId;
                    var flag = 0;
                    var flagType = flag & 15;
                    var skillObject = SkillObject.GetByType((SkillObjectType)flagType);
                    var skill = new Skill(SkillManager.Instance.GetSkillTemplate(skillId)); // TODO переделать...
                    skill.Use(npc, skillCaster, skillCastTarget, skillObject);

                    LoopAuto(npc);
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
