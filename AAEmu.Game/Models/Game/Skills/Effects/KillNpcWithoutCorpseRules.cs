namespace AAEmu.Game.Models.Game.Skills.Effects;

/// <summary>
/// Who a kill-without-corpse row may remove. The row names a template and a radius; vanish means
/// that matching caster may also disappear, not "delete whoever is applying the effect".
/// </summary>
public static class KillNpcWithoutCorpseRules
{
    /// <summary>
    /// True when this unit is one of the row's victims.
    /// </summary>
    /// <remarks>
    /// A vanish row whose <paramref name="effectNpcId"/> is unset is a self-remove (the caster
    /// disappears). A vanish row that names a template still has to match that template — a nearby
    /// cleanup skill listing other templates must not take a different unit with it.
    /// </remarks>
    public static bool IsVictim(
        uint effectNpcId,
        bool vanish,
        uint unitTemplateId,
        bool unitIsCaster,
        bool unitIsDead,
        bool inRadius)
    {
        if (unitIsDead)
            return false;

        if (effectNpcId == 0)
            return vanish && unitIsCaster;

        if (unitTemplateId != effectNpcId)
            return false;

        return inRadius || (vanish && unitIsCaster);
    }
}
