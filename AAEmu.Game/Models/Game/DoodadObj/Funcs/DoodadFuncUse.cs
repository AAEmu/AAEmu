using System;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Tasks.Skills;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncUse : DoodadFuncTemplate
    {
        public uint SkillId { get; set; }

        public override void Use(Unit caster, Doodad owner, uint skillId, int nextPhase = 0)
        {
            //TODO check skill refrences and consume items if items are required for skills
            // Make caster cast skill ? 
            if (SkillId > 0)
            {
                var skillTemplate = SkillManager.Instance.GetSkillTemplate(SkillId);
                if (skillTemplate == null)
                {
                    owner.ToPhaseAndUse = false;
                    return;
                }
                var useSkill = new Skill(skillTemplate);
                TaskManager.Instance.Schedule(
                    new UseSkillTask(useSkill, caster, new SkillCasterUnit(caster.ObjId), owner,
                        new SkillCastDoodadTarget { ObjId = owner.ObjId }, null), TimeSpan.FromMilliseconds(0));
            }
            // TODO далее, после возврата, будет вызов GoToPhase
            //if (nextPhase > 0)
            //    owner.GoToPhase(null, nextPhase, skillId);

            owner.ToPhaseAndUse = skillId > 0;
        }
    }
}
