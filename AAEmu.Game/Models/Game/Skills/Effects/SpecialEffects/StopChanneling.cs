using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Tasks.Skills;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

/// <summary>
/// Ends the target's running channel early, the way CSStopCasting and a stun do, so its effects
/// never apply. The four shipped rows carry no values. Three sit on plots: the start event of the
/// ranged counters 3734 and 6961 (skills 39989 and 50421, themselves not channels) on
/// original_source, and event 46299 of cinematic 5154 on original_source right before a
/// finish_channeling (81). The fourth is buff 27645's trigger on event 23 (any). In every case the
/// unit whose channel stops is the effect's resolved target.
/// </summary>
/// <remarks>
/// Only the skill-level channel (<see cref="EndChannelingTask"/>) is stopped. A plot's own channel
/// edge is ended by finish_channeling, which follows this effect in the one plot that reaches it
/// after that edge, so nothing here touches <c>ActivePlotState</c>.
/// </remarks>
public class StopChanneling : SpecialEffectAction
{
    public override void Execute(BaseUnit caster,
        SkillCaster casterObj,
        BaseUnit target,
        SkillCastTarget targetObj,
        CastAction castObj,
        Skill skill,
        SkillObject skillObject,
        DateTime time,
        int value1,
        int value2,
        int value3,
        int value4)
    {
        if (target is not Unit unit)
            return;
        if (unit.SkillTask is not EndChannelingTask channel)
            return;

        // Same call Unit.InterruptSkills makes: Skill.Stop marks the skill cancelled, tears the
        // channel down without applying its effects and tells the client and the zone.
        channel.Skill.Stop(unit, channel._channelDoodad);
    }
}
