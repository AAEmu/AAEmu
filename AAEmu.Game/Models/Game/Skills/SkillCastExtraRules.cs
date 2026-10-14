using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// Which SkillCastExtra bodies a cast actually carries. The flag byte is a bit mask on many
/// skills, so the same numeric type can mean "typed payload" or "no body" depending on the skill.
/// </summary>
public static class SkillCastExtraRules
{
    /// <summary>Wire size of type 6: <c>u64 supportItemId</c> + <c>bool autoUseAAPoint</c>.</summary>
    public const int GradeEnchantSupportBodyBytes = sizeof(ulong) + 1;

    /// <summary>
    /// Whether flag type 6 is the regrade charm body for this skill (always present when the
    /// type is set). Tempering also uses type 6, but only when a support additive is slotted.
    /// </summary>
    public static bool ReadsGradeEnchantSupport(SkillTemplate skill) =>
        skill?.Effects != null &&
        skill.Effects.Any(effect =>
            effect.Template is SpecialEffect special &&
            special.SpecialEffectTypeId == SpecialType.GradeEnchant);

    /// <summary>Tempering polish skills — type 6 body is optional (support additive).</summary>
    public static bool CarriesOptionalGradeEnchantSupport(SkillTemplate skill) =>
        skill?.Effects != null &&
        skill.Effects.Any(effect =>
            effect.Template is SpecialEffect special &&
            special.SpecialEffectTypeId == SpecialType.ItemRefurbishment);

    /// <summary>
    /// Whether the cast stream still holds a type-6 support body worth reading.
    /// Regrade always does; tempering only when an additive was placed (<c>leftBytes</c> covers
    /// the <c>u64</c>+<c>bool</c>). Bare tempering sets flag 6 with no body — reading it would
    /// steal <c>inputDirection</c> and leave the piece drawn as invalid after SkillStarted.
    /// </summary>
    public static bool CanReadGradeEnchantSupportBody(SkillTemplate skill, int leftBytes) =>
        ReadsGradeEnchantSupport(skill)
        || (CarriesOptionalGradeEnchantSupport(skill) && leftBytes >= GradeEnchantSupportBodyBytes);

    /// <summary>
    /// Whether the next byte after a socket-extract index is the <c>isAll</c> flag.
    /// Single-socket casts omit it (index only); extract-all writes <c>true</c> before
    /// <c>inputDirection</c>, so at least two bytes remain.
    /// </summary>
    public static bool SocketExtractHasIsAllByte(int bytesLeftAfterIndex) =>
        bytesLeftAfterIndex >= 2;
}
