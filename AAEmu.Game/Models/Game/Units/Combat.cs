using System;
using System.Collections.Generic;
using System.Text;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units.Route;

namespace AAEmu.Game.Models.Game.Units
{
    class Combat:Patrol
    {
        float distance = 1.5f;
        public override void Execute(Npc npc)
        {
            // 先判断距离
            // First judge the distance
            var move = false;
            var x = npc.Position.X - npc.CurrentTarget.Position.X;
            var y = npc.Position.Y - npc.CurrentTarget.Position.Y;
            var z = npc.Position.Z - npc.CurrentTarget.Position.Z;
            var MaxXYZ = Math.Max(Math.Max(Math.Abs(x), Math.Abs(y)), Math.Abs(z));

            // If we are killed, the NPC goes to the place of spawn
            var trg = (Unit)npc.CurrentTarget;
            if (trg?.Hp <= 0)
            {
                npc.BroadcastPacket(new SCCombatClearedPacket(npc.CurrentTarget.ObjId), true);
                npc.BroadcastPacket(new SCCombatClearedPacket(npc.ObjId), true);
                npc.CurrentTarget = null;
                npc.StartRegen();
                npc.BroadcastPacket(new SCTargetChangedPacket(npc.ObjId, 0), true);

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
                // 如果最大值超过distance 则放弃攻击转而进行追踪
                // If the maximum value exceeds distance, the attack is abandoned and the tracking is followed.
                if (MaxXYZ > distance)
                {
                    var track = new Track();
                    track.Pause(npc);
                    track.LastPatrol = LastPatrol;
                    LastPatrol = track;
                    Stop(npc);
                }
                else
                {
                    LoopDelay = 3000;

                    var skillId = 2u;

                    var skillCasterType = 0; // кто применяет
                    var skillCaster = SkillCaster.GetByType((SkillCasterType)skillCasterType);
                    skillCaster.ObjId = npc.ObjId;

                    var skillCastTargetType = 0; // на кого применяют
                    var skillCastTarget = SkillCastTarget.GetByType((SkillCastTargetType)skillCastTargetType);
                    skillCastTarget.ObjId = npc.CurrentTarget.ObjId;

                    var flag = 0;
                    var flagType = flag & 15;
                    var skillObject = SkillObject.GetByType((SkillObjectType)flagType);
                    if (flagType > 0) skillObject.Flag = SkillObjectType.None;


                    var skill = new Skill(SkillManager.Instance.GetSkillTemplate(skillId)); // TODO переделать...
                    skill.Use(npc, skillCaster, skillCastTarget, skillObject);

                    LoopAuto(npc);
                }
            }
        }
    }
}
