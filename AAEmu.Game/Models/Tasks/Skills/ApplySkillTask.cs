using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Tasks.Skills;

public class ApplySkillTask(
    Skill skill,
    BaseUnit caster,
    SkillCaster casterCaster,
    BaseUnit target,
    SkillCastTarget targetCaster,
    SkillObject skillObject)
    : Task
{
    public override void Execute()
    {
        if (skill.Cancelled)
        {
            if (skill.TlId != 0)
            {
                AAEmu.Game.Core.Managers.Id.SkillTlIdManager.ReleaseId(skill.TlId);
                skill.TlId = 0;
            }
            return;
        }
        skill.ApplyEffects(caster, casterCaster, target, targetCaster, skillObject);
        skill.EndSkill(caster);
    }
}
