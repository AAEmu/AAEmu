using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class GlobalCooldown : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.GlobalCooldown;

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
        if (caster is Character)
        {
            Logger.Debug("Special effects: GlobalCooldown value1 {0}, value2 {1}, value3 {2}, value4 {3}",
                value1, value2, value3, value4);
        }

        // A zero-valued plot effect means "no plot override"; it must not erase the shared GCD
        // already armed from default_gcd/custom_gcd by the skill cast.
        if (value1 == 0)
            return;

        // default_gcd takes precedence over plot-authored GCD values. The skill cast has already
        // armed its shared GCD before plot effects execute, so leave that value intact here.
        if (skill?.Template.DefaultGcd == true)
            return;

        if (caster is not Unit unit)
            return;

        var plotGcdMs = skill?.ResolvePlotGlobalCooldownMilliseconds(unit, value1) ?? value1;
        // For skills without default_gcd, nonzero plot-authored durations replace the shared
        // skill/weapon GCD and receive weapon-speed and attack-speed scaling.
        unit.GlobalCooldown = DateTime.UtcNow.AddMilliseconds(plotGcdMs);
    }
}
