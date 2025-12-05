using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Tasks.Skills;

public class CastTask(
    Skill skill,
    BaseUnit caster,
    SkillCaster casterCaster,
    BaseUnit target,
    SkillCastTarget targetCaster,
    SkillObject skillObject)
    : SkillTask(skill)
{
    public override void Execute()
    {
        if (Skill.Cancelled)
            return;

        Skill.Cast(caster, casterCaster, target, targetCaster, skillObject);
    }
}
