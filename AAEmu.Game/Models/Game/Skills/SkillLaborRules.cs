namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// Labor affordability for a cast. <c>skills.consume_lp</c> is the price; the charge itself happens in
/// <see cref="Skill.EndSkill"/> through <see cref="Skill.TryConsumeLabor"/>, which is idempotent per cast.
/// </summary>
/// <remarks>
/// The debit was the only place labor was looked at, and it silently does nothing when the character
/// cannot pay: <c>TryConsumeLabor</c> returns false and the cast finishes exactly as if it had been
/// charged. A player with an empty labor bar could therefore keep casting every labor skill for free.
/// The check now also runs before the cast, so the client gets <c>NeedLaborPower</c> instead.
/// 3,338 of the 38,043 skills declare a <c>consume_lp</c>; none of them is an ability skill, so this
/// gate sits on the crafting, farming and trade-good skills rather than on combat.
///
/// Both pools count: the account's labor and the server-local pool the client labels "Online Labor"
/// are spent together by <c>Character.ChangeLabor</c>, so affordability is their sum.
/// </remarks>
public static class SkillLaborRules
{
    /// <summary>Whether the character can pay <paramref name="laborCost"/> from both pools.</summary>
    public static bool CanAfford(int laborCost, int laborPower, int localLaborPower)
    {
        if (laborCost <= 0)
            return true;

        // The debit is a short; a cost past that cannot be paid at all.
        if (laborCost > short.MaxValue)
            return false;

        return (long)laborPower + localLaborPower >= laborCost;
    }
}
