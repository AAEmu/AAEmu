using System.Linq.Expressions;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncFakeUse : DoodadFuncTemplate
    {
        public uint SkillId { get; set; }
        public uint FakeSkillId { get; set; }
        public bool TargetParent { get; set; }

        public override void Use(Unit caster, Doodad owner, uint skillId, int nextPhase = 0)
        {
            if(SkillId != 0)
            {
                var skillCaster = SkillCaster.GetByType(SkillCasterType.Doodad);
                skillCaster.ObjId = owner.ObjId;

                var target = SkillCastTarget.GetByType(SkillCastTargetType.Unit);
                target.ObjId = caster.ObjId;
                if (TargetParent)
                {
                    //target owner/doodad
                    target = SkillCastTarget.GetByType(SkillCastTargetType.Doodad);
                    target.ObjId = owner.ObjId;

                }

                var skill = new Skill(SkillManager.Instance.GetSkillTemplate(SkillId));
                skill.Use(caster, skillCaster, target);             
               
                
            }
            if(FakeSkillId != 0)
            {
                // var skillCaster = SkillCaster.GetByType(SkillCasterType.Unit);
                // skillCaster.ObjId = caster.ObjId;
                //
                // var target = SkillCastTarget.GetByType(SkillCastTargetType.Doodad);
                // target.ObjId = owner.ObjId;
                // if (TargetParent)
                // {
                //     //target owner/doodad
                //     target = SkillCastTarget.GetByType(SkillCastTargetType.Doodad);
                //     target.ObjId = owner.ObjId;
                //
                // }
                //
                // var fakeSkill = new Skill(SkillManager.Instance.GetSkillTemplate(FakeSkillId));
                // fakeSkill.Use(caster, skillCaster, target);    
                if (FakeSkillId == skillId)
                    owner.GoToPhaseAndUse(caster, nextPhase, skillId);
            }

        }
    }
}
