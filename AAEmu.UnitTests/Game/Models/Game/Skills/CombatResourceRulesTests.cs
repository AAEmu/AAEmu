using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// <c>max_combat_resource</c> (215) as a signed delta on a pool's <c>combat_resources.max</c>.
/// </summary>
public class CombatResourceRulesTests
{
    // combat_resources: 광란 has max 5, 근성 5000, 마력의 원천 60.
    private const int FightMax = 5;
    private const int AdamantMax = 5000;

    [Test]
    public async Task Ceiling_WithoutARow_IsTheResourcesOwnMax()
    {
        // The exact-equality pin for Unit.AddCombatResource, which clamps against this.
        await Assert.That(CombatResourceRules.Ceiling(FightMax, 0)).IsEqualTo(FightMax);
        await Assert.That(CombatResourceRules.Ceiling(AdamantMax, 0)).IsEqualTo(AdamantMax);
    }

    [Test]
    public async Task Ceiling_AddsBuff22278sPlusOneToFight()
    {
        // Buff 22278 (정복) stores 1 and reads "광란의 중첩 개수가 1개 증가합니다".
        await Assert.That(CombatResourceRules.Ceiling(FightMax, 1)).IsEqualTo(6);
    }

    [Test]
    public async Task Ceiling_NeverGoesBelowZero()
    {
        // The -2 and -100…-500 rows would otherwise hand AddCombatResource a negative ceiling, which
        // Math.Clamp would answer by pinning every pool to 0.
        await Assert.That(CombatResourceRules.Ceiling(FightMax, -2)).IsEqualTo(3);
        await Assert.That(CombatResourceRules.Ceiling(FightMax, -100)).IsEqualTo(0);
        await Assert.That(CombatResourceRules.Ceiling(FightMax, -500)).IsEqualTo(0);
    }

    [Test]
    public async Task Ceiling_LeavesAnUnknownResourceWithoutOne()
    {
        // CombatResourceGameData.GetMax answers 0 for an id it does not know, and the caller treats that as
        // "no ceiling to clamp against". A unit bonus must not invent one.
        await Assert.That(CombatResourceRules.Ceiling(0, 0)).IsEqualTo(0);
        await Assert.That(CombatResourceRules.Ceiling(0, 1500)).IsEqualTo(0);
        await Assert.That(CombatResourceRules.Ceiling(-1, 1500)).IsEqualTo(0);
    }
}
