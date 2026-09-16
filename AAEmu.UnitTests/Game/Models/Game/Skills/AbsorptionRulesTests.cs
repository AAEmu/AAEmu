using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// <c>buffs.damage_absorption_type_id</c> (231 rows) and <c>buffs.damage_absorption_per_hit</c> (80 rows):
/// what one hit costs a shield.
/// </summary>
public class AbsorptionRulesTests
{
    // enum_damage_absorption_type
    private const uint None = 0;
    private const uint Count = 1;
    private const uint Amount = 2;

    [Test]
    public async Task IsShield_NeedsEitherColumn()
    {
        // 30,423 rows author neither and must stay out of the damage path.
        await Assert.That(AbsorptionRules.IsShield(None, 0)).IsFalse();

        await Assert.That(AbsorptionRules.IsShield(Count, 0)).IsTrue();   // the nine 은신 rows
        await Assert.That(AbsorptionRules.IsShield(Amount, 0)).IsTrue();  // 1011 보호막
        await Assert.That(AbsorptionRules.IsShield(None, 10_000)).IsTrue(); // 389 마상 수비
    }

    [Test]
    public async Task Apply_TypeZeroWithoutAPerHit_ChangesNothing()
    {
        var outcome = AbsorptionRules.Apply(None, 0, 50, 120);

        await Assert.That(outcome.Remaining).IsEqualTo(120);
        await Assert.That(outcome.Absorbed).IsEqualTo(0);
        await Assert.That(outcome.Charge).IsEqualTo(50);
        await Assert.That(outcome.Consumed).IsFalse();
    }

    [Test]
    public async Task Apply_TypeZeroWithAPerHit_IsACeilingWithNoPool()
    {
        // 389 마상 수비 / 430 돌파: per_hit 10,000 for the buff's 5 s, charge does not move.
        var small = AbsorptionRules.Apply(None, 10_000, 1, 4_000);
        await Assert.That(small.Absorbed).IsEqualTo(4_000);
        await Assert.That(small.Remaining).IsEqualTo(0);
        await Assert.That(small.Charge).IsEqualTo(1);
        await Assert.That(small.Consumed).IsFalse();

        // A hit larger than the ceiling only has its first 10,000 taken.
        var large = AbsorptionRules.Apply(None, 10_000, 1, 25_000);
        await Assert.That(large.Absorbed).IsEqualTo(10_000);
        await Assert.That(large.Remaining).IsEqualTo(15_000);
        await Assert.That(large.Consumed).IsFalse();
    }

    [Test]
    public async Task Apply_Count_SpendsOneHitAndAbsorbsUpToTheCeiling()
    {
        // 127 의기 충전: per_hit 5,000, charge 1, max_charge 3 — "피해를 입을 시 5000 이하의 피해 완전
        // 흡수 후 해제".
        var absorbed = AbsorptionRules.Apply(Count, 5_000, 1, 4_000);
        await Assert.That(absorbed.Absorbed).IsEqualTo(4_000);
        await Assert.That(absorbed.Remaining).IsEqualTo(0);
        await Assert.That(absorbed.Charge).IsEqualTo(0);
        await Assert.That(absorbed.Consumed).IsTrue();

        // 27892 피안의 편린: per_hit 7,000, charge 10 — ten hits, whatever they are worth.
        var first = AbsorptionRules.Apply(Count, 7_000, 10, 3_000);
        await Assert.That(first.Charge).IsEqualTo(9);
        await Assert.That(first.Consumed).IsFalse();

        var last = AbsorptionRules.Apply(Count, 7_000, 1, 3_000);
        await Assert.That(last.Charge).IsEqualTo(0);
        await Assert.That(last.Consumed).IsTrue();
    }

    [Test]
    public async Task Apply_Count_CapsTheHitAtThePerHitAmount()
    {
        // 27977 피안의 정수: per_hit 10,000.
        var outcome = AbsorptionRules.Apply(Count, 10_000, 15, 50_000);

        await Assert.That(outcome.Absorbed).IsEqualTo(10_000);
        await Assert.That(outcome.Remaining).IsEqualTo(40_000);
        await Assert.That(outcome.Charge).IsEqualTo(14);
    }

    [Test]
    public async Task Apply_CountWithNoPerHit_StillCostsOneHit()
    {
        // The nine 은신 rows: type 1, per_hit 0, charge 0. They swallow nothing and end on the first hit,
        // which is what "해당 강화는 타격시 해제됩니다" means and what the old arithmetic did too.
        var stealth = AbsorptionRules.Apply(Count, 0, 0, 900);
        await Assert.That(stealth.Absorbed).IsEqualTo(0);
        await Assert.That(stealth.Remaining).IsEqualTo(900);
        await Assert.That(stealth.Charge).IsEqualTo(0);
        await Assert.That(stealth.Consumed).IsTrue();
    }

    [Test]
    public async Task Apply_Amount_SpendsThePool()
    {
        // 1011 보호막: charge 103, per_hit 0 — "103 의 피해를 흡수하면 사라집니다".
        var partial = AbsorptionRules.Apply(Amount, 0, 103, 40);
        await Assert.That(partial.Absorbed).IsEqualTo(40);
        await Assert.That(partial.Remaining).IsEqualTo(0);
        await Assert.That(partial.Charge).IsEqualTo(63);
        await Assert.That(partial.Consumed).IsFalse();

        var exact = AbsorptionRules.Apply(Amount, 0, 103, 103);
        await Assert.That(exact.Absorbed).IsEqualTo(103);
        await Assert.That(exact.Remaining).IsEqualTo(0);
        await Assert.That(exact.Consumed).IsTrue();

        var over = AbsorptionRules.Apply(Amount, 0, 103, 500);
        await Assert.That(over.Absorbed).IsEqualTo(103);
        await Assert.That(over.Remaining).IsEqualTo(397);
        await Assert.That(over.Consumed).IsTrue();
    }

    [Test]
    public async Task Apply_Amount_PoolIsTheChargeAndNeverThePerHit()
    {
        // Every one of the 213 amount rows that authors no per_hit has to behave exactly as the plain
        // charge arithmetic did: absorb min(damage, charge), floor the charge, keep the remainder.
        for (var charge = 0; charge <= 60; charge++)
        {
            for (var damage = 0; damage <= 60; damage++)
            {
                var outcome = AbsorptionRules.Apply(Amount, 0, charge, damage);
                await Assert.That(outcome.Absorbed).IsEqualTo(Math.Min(charge, damage));
                await Assert.That(outcome.Remaining).IsEqualTo(Math.Max(0, damage - charge));
                await Assert.That(outcome.Charge).IsEqualTo(Math.Max(0, charge - damage));
                await Assert.That(outcome.Consumed).IsEqualTo(outcome.Charge <= 0);
            }
        }
    }

    [Test]
    public async Task Apply_Amount_WithAPerHitAndNoPool_UsesThePerHitAsThePool()
    {
        // 79 빛의 보호막 (2레벨): charge 0, per_hit 315 — "315의 피해를 흡수한다".
        var shield79 = AbsorptionRules.Apply(Amount, 315, 0, 500);
        await Assert.That(shield79.Absorbed).IsEqualTo(315);
        await Assert.That(shield79.Remaining).IsEqualTo(185);
        await Assert.That(shield79.Charge).IsEqualTo(0);
        await Assert.That(shield79.Consumed).IsTrue();

        // 221 빛의 갑옷 (1레벨): charge 1, per_hit 296 — "1분간 296의 피해를 흡수한다".
        var shield221 = AbsorptionRules.Apply(Amount, 296, 1, 100);
        await Assert.That(shield221.Absorbed).IsEqualTo(100);
        await Assert.That(shield221.Charge).IsEqualTo(196);
        await Assert.That(shield221.Consumed).IsFalse();

        // A hit smaller than the per-hit ceiling is all it takes.
        var under = AbsorptionRules.Apply(Amount, 296, 1, 50);
        await Assert.That(under.Absorbed).IsEqualTo(50);
        await Assert.That(under.Charge).IsEqualTo(246);
    }

    [Test]
    public async Task Apply_Amount_LeavesTheChargeAloneWhenNothingWasAbsorbed()
    {
        // A zero-damage hit must not inflate a per_hit-only shield's charge into existence.
        var nothing = AbsorptionRules.Apply(Amount, 296, 1, 0);
        await Assert.That(nothing.Absorbed).IsEqualTo(0);
        await Assert.That(nothing.Charge).IsEqualTo(1);
        await Assert.That(nothing.Consumed).IsFalse();
    }

    [Test]
    public async Task Apply_NeverInventsDamageOrCharge()
    {
        foreach (var type in new[] { None, Count, Amount })
        {
            var zero = AbsorptionRules.Apply(type, 5_000, 5, 0);
            await Assert.That(zero.Absorbed).IsEqualTo(0);
            await Assert.That(zero.Remaining).IsEqualTo(0);
            await Assert.That(zero.Charge).IsGreaterThanOrEqualTo(0);

            var negative = AbsorptionRules.Apply(type, 5_000, -10, -100);
            await Assert.That(negative.Absorbed).IsEqualTo(0);
            await Assert.That(negative.Remaining).IsEqualTo(0);
            await Assert.That(negative.Charge).IsGreaterThanOrEqualTo(0);
        }
    }
}
