using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Static;

namespace AAEmu.Game.Models.Game.Expeditions;

internal enum GuildBuffDurationSemantics
{
    UnknownGrade,
    NotAuthored,
    AppliedThroughBuffModifier
}

/// <summary>
/// The shipped grade table has no expiry/TTL column. A grade can nevertheless carry authored
/// duration modifiers through <c>buff_modifiers</c>; this classification keeps that distinction
/// typed instead of inventing a timer for the grade purchase itself.
/// </summary>
internal static class GuildBuffDurationRules
{
    public static GuildBuffDurationSemantics Classify(
        ExpeditionBuffGrade grade,
        IReadOnlyCollection<BuffModifier> modifiers)
    {
        if (grade == null)
            return GuildBuffDurationSemantics.UnknownGrade;

        return modifiers.Any(modifier => modifier.BuffAttribute == BuffAttribute.Duration)
            ? GuildBuffDurationSemantics.AppliedThroughBuffModifier
            : GuildBuffDurationSemantics.NotAuthored;
    }
}
