namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// How much labor a ConsumeLaborPower (special type 86) row takes.
/// </summary>
/// <remarks>
/// content, 10.0.2.13 game_decrypted: 71 rows and every one of them is all zero, including <c>value1</c> —
/// so the slot can carry an amount but no shipped row says how it is scaled. None of the 71 is referenced by
/// an <c>effects</c> row either, so the type cannot be reached from content today and the live labor charge
/// is <c>skills.consume_labor_power</c> in <c>Skill.EndSkill</c>. A row that does set <c>value1</c> uses it;
/// zero keeps the template's cost, which is what the shipped rows do now. The 10.0.2.13 client Lua does not
/// name the slot either — a search of every script under <c>game/scripts</c> for <c>labor_power</c> finds
/// only bar textures, a display name and an <c>exp_by_labor_power_mul</c> tooltip format — so how a non-zero
/// <c>value1</c> should be read stays unknown.
/// </remarks>
public static class LaborPowerRules
{
    public static int ResolveCost(int value1, int templateCost) =>
        value1 > 0 ? value1 : Math.Max(0, templateCost);
}
