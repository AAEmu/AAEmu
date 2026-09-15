using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// The attack-speed ratings — <c>attack_speed_mul</c> (218) and the melee/ranged/animation views 54/55/119 —
/// as the factor an attack interval is multiplied by.
/// </summary>
/// <remarks>
/// The load-bearing property is that the arithmetic is the one <c>Character.GlobalCooldownMul</c> already
/// applies to <c>global_cooldown_mul</c> (74): the 377 rows where 54/55/119 store the same value as 74 have
/// to come out at the same factor, or consuming them would silently rescale every weapon swing in the game.
/// The factors below are compared within a hair of a percent because they pass through the same float
/// division the existing getter uses, not to hide a difference.
/// </remarks>
public class SpeedMultiplierRulesTests
{
    private const double Epsilon = 1e-6;

    [Test]
    public async Task DelayFactor_WithoutARating_IsExactlyOne()
    {
        await Assert.That(SpeedMultiplierRules.DelayFactor(0)).IsEqualTo(1.0);
    }

    [Test]
    public async Task DelayFactor_UsesTheGlobalCooldownMulArithmetic()
    {
        // Character.GlobalCooldownMul is (int)(100000f / (res + 1000f)), divided by 100. -700 is the row
        // behind buff 4343 공속테스트 (느려짐) and its clipped form -666 is what the tooltip's "공속이 66%
        // 줄어듭니다" describes, so the clipped interval is 2.99x the base one.
        await Assert.That(SpeedMultiplierRules.DelayFactor(-666)).IsEqualTo(2.99).Within(Epsilon);
        await Assert.That(SpeedMultiplierRules.DelayFactor(-700)).IsEqualTo(2.99).Within(Epsilon); // clipped to -666
        await Assert.That(SpeedMultiplierRules.DelayFactor(-75)).IsEqualTo(1.08).Within(Epsilon);
        await Assert.That(SpeedMultiplierRules.DelayFactor(500)).IsEqualTo(0.66).Within(Epsilon);
        await Assert.That(SpeedMultiplierRules.DelayFactor(1000)).IsEqualTo(0.5).Within(Epsilon);
    }

    [Test]
    public async Task ClampRating_KeepsTheUnitAttributeLimitsRange()
    {
        // unit_attribute_limits rows 1, 3, 4, 5 and 46 all hold minimum -666, maximum 2000.
        await Assert.That(SpeedMultiplierRules.ClampRating(-900)).IsEqualTo(-666);
        await Assert.That(SpeedMultiplierRules.ClampRating(4000)).IsEqualTo(2000);
        await Assert.That(SpeedMultiplierRules.ClampRating(-666)).IsEqualTo(-666);
        await Assert.That(SpeedMultiplierRules.ClampRating(2000)).IsEqualTo(2000);
        await Assert.That(SpeedMultiplierRules.ClampRating(0)).IsEqualTo(0);
    }

    [Test]
    public async Task DelayFactor_StaysPositiveAtBothEndsOfTheRange()
    {
        // A row of -900 or +4000 would otherwise produce a zero or a negative interval.
        await Assert.That(SpeedMultiplierRules.DelayFactor(long.MinValue)).IsGreaterThan(0.0);
        await Assert.That(SpeedMultiplierRules.DelayFactor(long.MinValue)).IsEqualTo(2.99).Within(Epsilon);
        await Assert.That(SpeedMultiplierRules.DelayFactor(long.MaxValue)).IsEqualTo(0.33).Within(Epsilon);
    }

    [Test]
    public async Task AttackIntervalFactor_WithoutAnyRating_IsTheCallersOwnFactor()
    {
        // The weapon-speed branch passes GlobalCooldownMul / 100 here; a unit with none of 218/54/55 must
        // keep that number untouched, so the fallback is returned as the same double it arrived as.
        await Assert.That(SpeedMultiplierRules.AttackIntervalFactor(0, 0, 1.0)).IsEqualTo(1.0);
        await Assert.That(SpeedMultiplierRules.AttackIntervalFactor(0, 0, 1.5)).IsEqualTo(1.5);
        await Assert.That(SpeedMultiplierRules.AttackIntervalFactor(0, 0, 0.123456789)).IsEqualTo(0.123456789);
    }

    [Test]
    public async Task AttackIntervalFactor_HasAttackSpeedWinOverThePerTypeView()
    {
        // Only three buff rows carry 218 next to 54/55 and four next to 74, so the two are never meant to
        // compound; the newer general rating wins.
        await Assert.That(SpeedMultiplierRules.AttackIntervalFactor(1000, 500, 1.0)).IsEqualTo(0.5).Within(Epsilon);
        await Assert.That(SpeedMultiplierRules.AttackIntervalFactor(0, 500, 1.0)).IsEqualTo(0.66).Within(Epsilon);
    }

    [Test]
    public async Task AttackIntervalFactor_IgnoresARatingOfZeroInEitherSlot()
    {
        await Assert.That(SpeedMultiplierRules.AttackIntervalFactor(0, 0, 2.0)).IsEqualTo(2.0);
        // A stored -1000 is clipped to the -666 limit rather than producing an infinite interval.
        await Assert.That(SpeedMultiplierRules.AttackIntervalFactor(0, -1000, 2.0)).IsEqualTo(2.99).Within(Epsilon);
        await Assert.That(SpeedMultiplierRules.AttackIntervalFactor(-1000, 0, 2.0)).IsEqualTo(2.99).Within(Epsilon);
    }

    [Test]
    public async Task AnimationFactor_FallsBackToTheFactorTheDelayAlreadyUsed()
    {
        // Skill.Use scales the fire-animation sync time by GlobalCooldownMul / 100; a 119 row replaces that,
        // and its absence restores it exactly.
        await Assert.That(SpeedMultiplierRules.AnimationFactor(0, 3.33)).IsEqualTo(3.33);
        await Assert.That(SpeedMultiplierRules.AnimationFactor(0, 1.0)).IsEqualTo(1.0);
        await Assert.That(SpeedMultiplierRules.AnimationFactor(-700, 1.0)).IsEqualTo(2.99).Within(Epsilon);
    }
}
