using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Tasks.Skills;

public class CastTask : SkillTask
{
    private readonly BaseUnit _caster;
    private readonly SkillCaster _casterCaster;
    private readonly BaseUnit _target;
    private readonly SkillCastTarget _targetCaster;
    private readonly SkillObject _skillObject;

    public CastTask(Skill skill, BaseUnit caster, SkillCaster casterCaster, BaseUnit target, SkillCastTarget targetCaster, SkillObject skillObject) : base(skill)
    {
        _caster = caster;
        _casterCaster = casterCaster;
        _target = target;
        _targetCaster = targetCaster;
        _skillObject = skillObject;
    }

    public override System.Threading.Tasks.Task ExecuteAsync()
    {
        if (Skill.Cancelled)
            return System.Threading.Tasks.Task.CompletedTask;

        Skill.Cast(_caster, _casterCaster, _target, _targetCaster, _skillObject);

        return System.Threading.Tasks.Task.CompletedTask;
    }
}
