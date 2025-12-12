using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Tasks.Skills;

#pragma warning disable IDE0052 // Remove unread private members

public class EndChannelingTask(
    Skill skill,
    BaseUnit caster,
    SkillCaster casterCaster,
    BaseUnit target,
    SkillCastTarget targetCaster,
    SkillObject skillObject,
    Doodad channelDoodad)
    : SkillTask(skill)
{
    private readonly BaseUnit _target = target;
    private readonly SkillCastTarget _targetCaster = targetCaster;
    private readonly SkillObject _skillObject = skillObject;
    public Doodad _channelDoodad { get; set; } = channelDoodad;

    public override void Execute()
    {
        // Skill.ScheduleEffects(_caster, _casterCaster, _target, _targetCaster, _skillObject);
        Skill.EndChanneling(caster, _channelDoodad, casterCaster);
    }
}
