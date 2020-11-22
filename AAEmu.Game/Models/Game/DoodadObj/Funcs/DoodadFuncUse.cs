using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
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
            
            var skillTemplate = SkillManager.Instance.GetSkillTemplate(SkillId);
            if (skillTemplate == null)
                return;

            if (SkillId > 0)
            {
                var useSkill = new Skill(skillTemplate);
                TaskManager.Instance.Schedule(
                    new UseSkillTask(useSkill, caster, new SkillCasterUnit(caster.ObjId), owner,
                        new SkillCastDoodadTarget() {ObjId = owner.ObjId}, null), TimeSpan.FromMilliseconds(0));
                // owner.Use(caster);
            }
        }
    }
}
