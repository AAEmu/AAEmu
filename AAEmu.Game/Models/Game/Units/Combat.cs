using System;
using System.Collections.Generic;
using System.Text;
using AAEmu.Game.Core.Managers;
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
            //先判断距离
            bool move = false;
            float x = npc.Position.X - npc.CurrentTarget.Position.X;
            float y = npc.Position.Y - npc.CurrentTarget.Position.Y;
            float z = npc.Position.Z - npc.CurrentTarget.Position.Z;
            float MaxXYZ = Math.Max(Math.Max(Math.Abs(x), Math.Abs(y)), Math.Abs(z));
            //如果最大值超过distance 则放弃攻击转而进行追踪
            if (MaxXYZ > distance)
            {
                Track track = new Track();
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
