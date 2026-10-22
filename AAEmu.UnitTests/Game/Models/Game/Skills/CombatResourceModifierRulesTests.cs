using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// Pins the CombatResource <c>linear_level_bonus</c> scale the client already shows on the sheet.
/// </summary>
public class CombatResourceModifierRulesTests
{
    // Shipped 광란 (combat_resources id 1) unit_modifiers: attack_speed_mul linear 3000, crit bonus 5000.
    private const int DeliriumAttackSpeedLinear = 3000;
    private const int DeliriumCritLinear = 5000;

    [Test]
    public async Task EmptyPool_ContributesNothing()
    {
        await Assert.That(CombatResourceModifierRules.ScaledModifier(0, DeliriumAttackSpeedLinear, 0))
            .IsEqualTo(0L);
        await Assert.That(CombatResourceModifierRules.ScaledModifier(0, DeliriumAttackSpeedLinear, -1))
            .IsEqualTo(0L);
    }

    [Test]
    public async Task DeliriumAttackSpeed_MatchesClientSheetPerPoint()
    {
        // Client sheet: +30 attack_speed_mul per delirium point (241 + 5×30 = 391 at five stacks).
        await Assert.That(CombatResourceModifierRules.ScaledModifier(0, DeliriumAttackSpeedLinear, 1))
            .IsEqualTo(30L);
        await Assert.That(CombatResourceModifierRules.ScaledModifier(0, DeliriumAttackSpeedLinear, 5))
            .IsEqualTo(150L);
    }

    [Test]
    public async Task DeliriumCritBonus_ScalesTheSameWay()
    {
        await Assert.That(CombatResourceModifierRules.ScaledModifier(0, DeliriumCritLinear, 1))
            .IsEqualTo(50L);
        await Assert.That(CombatResourceModifierRules.ScaledModifier(0, DeliriumCritLinear, 5))
            .IsEqualTo(250L);
    }

    [Test]
    public async Task FlatValue_StillScalesThroughTheSharedFormula()
    {
        // Same path buff rows use when value is non-zero and linear is 0: amount is abLevel, stack 1.
        await Assert.That(CombatResourceModifierRules.ScaledModifier(40, 0, 3)).IsEqualTo(40L);
        await Assert.That(CombatResourceModifierRules.ScaledModifier(0, 4000, 3)).IsEqualTo(120L);
    }
}
