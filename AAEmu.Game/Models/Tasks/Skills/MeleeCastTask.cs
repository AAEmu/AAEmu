using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Tasks.Skills;

public class MeleeCastTask(
    Skill skill,
    BaseUnit caster,
    SkillCaster casterCaster,
    BaseUnit target,
    SkillCastTarget targetCaster,
    SkillObject skillObject)
    : SkillTask(skill)
{
    //private readonly uint _skillId;

    public override void Execute()
    {
        Skill.Cast(caster, casterCaster, target, targetCaster, skillObject);
    }
}
