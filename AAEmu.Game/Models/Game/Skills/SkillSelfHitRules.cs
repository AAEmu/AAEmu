using AAEmu.Game.Models.Game.Skills.Effects;

namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// A position cast whose area found no unit falls back to the caster so that spawn, doodad and other
/// utility effects still have an origin to act on. Damage and debuffs must not follow that fallback:
/// the cast was aimed at the ground, so the unit that aimed it is not one of its targets. Without this
/// guard a debuff thrown at an empty spot was applied to its own caster.
/// </summary>
public static class SkillSelfHitRules
{
    public static bool AllowsOriginFallbackTarget(bool isOriginFallbackOnly, uint casterObjId, uint targetObjId, SkillEffect effect)
    {
        if (!isOriginFallbackOnly || casterObjId != targetObjId)
            return true;

        return effect.Template switch
        {
            DamageEffect => false,
            BuffEffect buff => buff.Buff is not { Kind: BuffKind.Bad },
            _ => true
        };
    }
}
