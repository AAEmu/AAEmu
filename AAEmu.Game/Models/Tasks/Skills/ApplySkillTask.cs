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
        skill.ApplyEffects(caster, casterCaster, target, targetCaster, skillObject);
        skill.EndSkill(caster);
    }
}
