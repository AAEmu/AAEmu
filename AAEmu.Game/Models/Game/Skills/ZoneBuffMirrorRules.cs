using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// What World does with a <c>ZWCreateBuff</c> the zone authored. The zone has already applied the buff, so
/// the mirror is a notification rather than a cast — but it still has to go through the same refusals
/// <see cref="BuffTemplate.Apply"/> makes, or World ends up holding a second instance of a buff it already
/// has (and the client a second icon) while the zone holds one.
/// </summary>
/// <remarks>
/// The three cases are the ones <see cref="BuffTemplate.Apply"/> checks before it reaches
/// <c>Buffs.AddBuff</c>: the buff is already live, it requires a buff the unit does not carry, or it
/// requires a tag the unit does not carry. Everything else about that path (the ability level, the stack
/// rule, the zone relay) is unchanged.
/// </remarks>
public static class ZoneBuffMirrorRules
{
    public enum Outcome
    {
        Apply,
        AlreadyLive,
        MissingRequiredBuff,
        MissingRequiredTag
    }

    /// <summary>
    /// Whether the mirror may apply, given what the unit already carries. A buff with no id
    /// (<c>buff_effects.buff_id</c> naming no <c>buffs</c> row) is never "already live".
    /// </summary>
    public static Outcome Decide(uint buffId, bool alreadyLive, uint requiredBuffId, bool carriesRequiredBuff,
        long missingRequiredTag)
    {
        if (buffId != 0 && alreadyLive)
            return Outcome.AlreadyLive;
        if (requiredBuffId != 0 && !carriesRequiredBuff)
            return Outcome.MissingRequiredBuff;
        if (missingRequiredTag > 0)
            return Outcome.MissingRequiredTag;
        return Outcome.Apply;
    }

    public static bool ShouldApply(uint buffId, bool alreadyLive, uint requiredBuffId, bool carriesRequiredBuff,
        long missingRequiredTag) =>
        Decide(buffId, alreadyLive, requiredBuffId, carriesRequiredBuff, missingRequiredTag) == Outcome.Apply;

    /// <summary>One line for the log, so a dropped mirror is visible without a packet capture.</summary>
    public static string Describe(Outcome outcome) => outcome switch
    {
        Outcome.AlreadyLive => "already live in World",
        Outcome.MissingRequiredBuff => "requires a buff the unit does not carry",
        Outcome.MissingRequiredTag => "requires a buff tag the unit does not carry",
        _ => "applied"
    };
}
