namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// The mana a <c>ManaCost</c> special effect takes. Kept as its own rule so the arithmetic is pinned by a
/// test, because the formula cannot be confirmed from the shipped content.
/// </summary>
/// <remarks>
/// content, 10.0.2.13 game_decrypted: 645 <c>special_effects</c> rows of type 39 and NOT ONE of them is
/// referenced by an <c>effects</c> row, so no skill, buff or trigger can reach this class' effect — the
/// special effect never runs. The live mana charge is <c>Skill.ManaCost</c> (Skill.cs), which reads
/// <c>skills.mana_cost</c> / <c>skills.mana_level_md</c> and the ability-level base of <c>formulas</c> 13.
/// <para>
/// The divisor below is the only part of the arithmetic with a history: it was added by PR #378
/// ("Fixed mana cost for Freezing Arrow skill") after the original code charged <c>value2</c> alone, and no
/// row, table or script states it. The <c>formulas</c> table (ids 2-72) has no mana row, and the 10.0.2.13
/// client Lua carries no skill mana-cost expression either — a search of every script under
/// <c>game/scripts</c> for <c>mana_cost</c> / <c>manaCost</c> finds only the status-bar and tooltip
/// scaffolding — so there is nothing left to check it against. It is left exactly as shipped: changing an
/// unreachable number would be a silent edit with no evidence behind it.
/// </para>
/// <para>
/// The code this replaced carried a bare "TODO / 10" next to the result, which would read the slots as
/// tenths of a point of mana. Nothing in the content supports or contradicts it, so it is recorded here
/// rather than acted on.
/// </para>
/// </remarks>
public static class ManaCostRules
{
    /// <summary>The divisor taken from a single skill in PR #378; unverified.</summary>
    public const double Value2Divisor = 6.35;

    /// <summary>
    /// Mana for the two value slots. <c>value1</c> is set on 78 of the 645 rows and <c>value2</c> on 393, and
    /// the two are never both non-zero in the same row, which is what the shape below assumes.
    /// </summary>
    public static double Compute(int value1, int value2) => value1 + value2 / Value2Divisor;
}
