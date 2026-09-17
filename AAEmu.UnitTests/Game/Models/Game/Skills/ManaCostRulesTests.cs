using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// ManaCost (type 39) charges the caster mana for a skill that carries its cost in the effect. The formula
/// cannot be confirmed from the content — none of the 645 rows is reachable and the client Lua has no
/// mana-cost expression — so the arithmetic is pinned here instead, exactly as shipped.
/// </summary>
public class ManaCostRulesTests
{
    [Test]
    public async Task AFlatValue_IsChargedAsIs()
    {
        await Assert.That(ManaCostRules.Compute(24, 0)).IsEqualTo(24d);
        await Assert.That(ManaCostRules.Compute(9, 0)).IsEqualTo(9d);
    }

    [Test]
    public async Task APerLevelValue_IsDividedByTheShippedDivisor()
    {
        // value1 is set on 78 of the 645 rows and value2 on 393; the two are never both non-zero.
        await Assert.That(ManaCostRules.Compute(0, 100)).IsEqualTo(100d / ManaCostRules.Value2Divisor);
        await Assert.That(ManaCostRules.Compute(0, 180)).IsEqualTo(180d / ManaCostRules.Value2Divisor);
    }

    [Test]
    public async Task TheDivisorIsTheOnePR378Introduced()
    {
        // No row, table or client script states it: changing it would be a silent edit with no evidence.
        await Assert.That(ManaCostRules.Value2Divisor).IsEqualTo(6.35d);
    }

    [Test]
    public async Task AnEmptyRow_ChargesNothing()
    {
        // 174 of the 645 rows carry two zeros.
        await Assert.That(ManaCostRules.Compute(0, 0)).IsEqualTo(0d);
    }
}
