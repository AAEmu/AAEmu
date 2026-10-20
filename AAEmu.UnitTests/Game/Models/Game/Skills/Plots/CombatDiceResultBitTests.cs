using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Plots;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Plots;

/// <summary>
/// <c>plot_conditions.param1</c> for kind CombatDiceResult is a bit mask over
/// <c>enum_combat_dice_results</c> (bit = id - 1). Plain mask 51 and NOT 204 are complements.
/// </summary>
public class CombatDiceResultBitTests
{
    // Shipped complements: connected (hit/crit/block/parry) vs avoided (miss/dodge/resist/immune).
    private const int ConnectedMask = 51;
    private const int AvoidedMask = 204;

    [Test]
    public async Task Bits_MatchEnumCombatDiceResultsOrder()
    {
        await Assert.That(PlotCondition.CombatDiceResultBit(SkillHitType.MeleeHit)).IsEqualTo(1 << 0);
        await Assert.That(PlotCondition.CombatDiceResultBit(SkillHitType.RangedHit)).IsEqualTo(1 << 0);
        await Assert.That(PlotCondition.CombatDiceResultBit(SkillHitType.SpellHit)).IsEqualTo(1 << 0);
        await Assert.That(PlotCondition.CombatDiceResultBit(SkillHitType.MeleeCritical)).IsEqualTo(1 << 1);
        await Assert.That(PlotCondition.CombatDiceResultBit(SkillHitType.RangedCritical)).IsEqualTo(1 << 1);
        await Assert.That(PlotCondition.CombatDiceResultBit(SkillHitType.SpellCritical)).IsEqualTo(1 << 1);
        await Assert.That(PlotCondition.CombatDiceResultBit(SkillHitType.MeleeMiss)).IsEqualTo(1 << 2);
        await Assert.That(PlotCondition.CombatDiceResultBit(SkillHitType.SpellMiss)).IsEqualTo(1 << 2);
        await Assert.That(PlotCondition.CombatDiceResultBit(SkillHitType.MeleeDodge)).IsEqualTo(1 << 3);
        await Assert.That(PlotCondition.CombatDiceResultBit(SkillHitType.MeleeBlock)).IsEqualTo(1 << 4);
        await Assert.That(PlotCondition.CombatDiceResultBit(SkillHitType.MeleeParry)).IsEqualTo(1 << 5);
        await Assert.That(PlotCondition.CombatDiceResultBit(SkillHitType.SpellResist)).IsEqualTo(1 << 6);
        await Assert.That(PlotCondition.CombatDiceResultBit(SkillHitType.Immune)).IsEqualTo(1 << 7);
    }

    [Test]
    public async Task ConnectedAndAvoidedMasks_AreComplements()
    {
        await Assert.That(ConnectedMask | AvoidedMask).IsEqualTo(255);
        await Assert.That(ConnectedMask & AvoidedMask).IsEqualTo(0);
    }

    [Test]
    public async Task PlainConnectedMask_PassesOnHitAndBlock_FailsOnMiss()
    {
        // Plain param1=51: follow-up effects (slow/combos) when the attack connected.
        await Assert.That(Matches(ConnectedMask, SkillHitType.RangedHit)).IsTrue();
        await Assert.That(Matches(ConnectedMask, SkillHitType.RangedCritical)).IsTrue();
        await Assert.That(Matches(ConnectedMask, SkillHitType.RangedBlock)).IsTrue();
        await Assert.That(Matches(ConnectedMask, SkillHitType.RangedParry)).IsTrue();
        await Assert.That(Matches(ConnectedMask, SkillHitType.RangedMiss)).IsFalse();
        await Assert.That(Matches(ConnectedMask, SkillHitType.RangedDodge)).IsFalse();
        await Assert.That(Matches(ConnectedMask, SkillHitType.SpellResist)).IsFalse();
        await Assert.That(Matches(ConnectedMask, SkillHitType.Immune)).IsFalse();
    }

    [Test]
    public async Task NotAvoidedMask_PassesOnHit_FailsOnMissAndSpellResist()
    {
        // NOT 204: same connected set once PlotCondition flips the raw result.
        await Assert.That(Matches(AvoidedMask, SkillHitType.MeleeHit)).IsFalse();
        await Assert.That(Matches(AvoidedMask, SkillHitType.MeleeMiss)).IsTrue();
        await Assert.That(Matches(AvoidedMask, SkillHitType.SpellResist)).IsTrue();
        await Assert.That(Matches(AvoidedMask, SkillHitType.Immune)).IsTrue();
        // Block/parry are not in 204 — NOT form therefore keeps them as connected.
        await Assert.That(Matches(AvoidedMask, SkillHitType.MeleeBlock)).IsFalse();
        await Assert.That(Matches(AvoidedMask, SkillHitType.MeleeParry)).IsFalse();
    }

    [Test]
    public async Task HitOrCriticalMask_ExcludesBlock()
    {
        const int hitOrCrit = 3;
        await Assert.That(Matches(hitOrCrit, SkillHitType.SpellHit)).IsTrue();
        await Assert.That(Matches(hitOrCrit, SkillHitType.SpellCritical)).IsTrue();
        await Assert.That(Matches(hitOrCrit, SkillHitType.MeleeBlock)).IsFalse();
        await Assert.That(Matches(hitOrCrit, SkillHitType.SpellMiss)).IsFalse();
    }

    private static bool Matches(int resultMask, SkillHitType hitType) =>
        (resultMask & PlotCondition.CombatDiceResultBit(hitType)) != 0;
}
