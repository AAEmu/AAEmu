using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// Which SkillCastExtra bodies a cast actually carries. The flag byte is a bit mask on many
/// skills, so the same numeric type can mean "typed payload" or "no body" depending on the skill.
/// </summary>
public static class SkillCastExtraRules
{
    /// <summary>
    /// Whether flag type 6 should be read as the regrade charm body (<c>u64</c> + <c>bool</c>).
    /// Tempering and other casts set bits 1+2 (= 6) with <em>no</em> body; reading the charm shape
    /// there steals <c>inputDirection</c> and desyncs SkillStarted so the piece draws as invalid.
    /// </summary>
    public static bool ReadsGradeEnchantSupport(SkillTemplate skill) =>
        skill?.Effects != null &&
        skill.Effects.Any(effect =>
            effect.Template is SpecialEffect special &&
            special.SpecialEffectTypeId == SpecialType.GradeEnchant);

    /// <summary>
    /// Whether the next byte after a socket-extract index is the <c>isAll</c> flag.
    /// Single-socket casts omit it (index only); extract-all writes <c>true</c> before
    /// <c>inputDirection</c>, so at least two bytes remain.
    /// </summary>
    public static bool SocketExtractHasIsAllByte(int bytesLeftAfterIndex) =>
        bytesLeftAfterIndex >= 2;
}
