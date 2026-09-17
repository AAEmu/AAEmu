using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// ExplodeBuff (type 46) burns the target's beneficial effects and pays damage per burned effect. Skill 내부
/// 충격 16410's own text ("적의 이로운 효과를 태워 소멸 시키며 … 의 피해를 주고") and buff 449's
/// ("이로운 효과가 존재할 경우 1개의 강화 효과 소멸 및 피해", with 1 in both slots) are what the slots read as.
/// </summary>
public class ExplodeBuffRulesTests
{
    [Test]
    public async Task TheLargerSlotIsTheCap()
    {
        // Both shipped rows set the two slots equal, so which one caps cannot be told apart.
        await Assert.That(ExplodeBuffRules.MaxBuffsToBurn(20, 20)).IsEqualTo(20);
        await Assert.That(ExplodeBuffRules.MaxBuffsToBurn(1, 1)).IsEqualTo(1);
        await Assert.That(ExplodeBuffRules.MaxBuffsToBurn(1, 20)).IsEqualTo(20);
        await Assert.That(ExplodeBuffRules.MaxBuffsToBurn(0, 0)).IsEqualTo(0);
        await Assert.That(ExplodeBuffRules.MaxBuffsToBurn(-4, 0)).IsEqualTo(0);
    }

    [Test]
    public async Task OnlyBeneficialNonPassiveNonSystemEffectsAreBurned()
    {
        var chosen = ExplodeBuffRules.SelectBuffs(
        [
            new ExplodeBuffRules.BurnCandidate(1, BuffKind.Good, Passive: false, System: false),
            new ExplodeBuffRules.BurnCandidate(2, BuffKind.Bad, Passive: false, System: false),
            new ExplodeBuffRules.BurnCandidate(3, BuffKind.Hidden, Passive: false, System: false),
            new ExplodeBuffRules.BurnCandidate(4, BuffKind.Good, Passive: true, System: false),
            new ExplodeBuffRules.BurnCandidate(5, BuffKind.Good, Passive: false, System: true),
            new ExplodeBuffRules.BurnCandidate(6, BuffKind.Good, Passive: false, System: false)
        ], maxCount: 20);

        await Assert.That(chosen.Select(buff => buff.Index)).IsEquivalentTo(new[] { 1, 6 });
    }

    [Test]
    public async Task TheCapLimitsTheBurn_LowestSlotFirst()
    {
        var buffs = new[]
        {
            new ExplodeBuffRules.BurnCandidate(9, BuffKind.Good, false, false),
            new ExplodeBuffRules.BurnCandidate(2, BuffKind.Good, false, false),
            new ExplodeBuffRules.BurnCandidate(5, BuffKind.Good, false, false)
        };

        var chosen = ExplodeBuffRules.SelectBuffs(buffs, maxCount: 2);

        await Assert.That(chosen.Select(buff => buff.Index)).IsEquivalentTo(new[] { 2, 5 });
    }

    [Test]
    public async Task NoCapOrNothingToBurn_SelectsNothing()
    {
        var buffs = new[] { new ExplodeBuffRules.BurnCandidate(1, BuffKind.Good, false, false) };

        await Assert.That(ExplodeBuffRules.SelectBuffs(buffs, maxCount: 0)).IsEmpty();
        await Assert.That(ExplodeBuffRules.SelectBuffs(buffs, maxCount: -1)).IsEmpty();
        await Assert.That(ExplodeBuffRules.SelectBuffs(null, maxCount: 5)).IsEmpty();
    }

    [Test]
    public async Task DamageIsPerBurnedEffect()
    {
        // Skill 16410 carries value1 1000 and skill 20650 the same, so the tooltip's min~max span is
        // 1000..20000 over the 1..20 effects the target can be holding.
        await Assert.That(ExplodeBuffRules.DamageFor(1, 1000)).IsEqualTo(1000);
        await Assert.That(ExplodeBuffRules.DamageFor(20, 1000)).IsEqualTo(20000);
        await Assert.That(ExplodeBuffRules.DamageFor(1, 2000)).IsEqualTo(2000);
    }

    [Test]
    public async Task NothingBurnedOrNoPerEffectDamage_DealsNothing()
    {
        await Assert.That(ExplodeBuffRules.DamageFor(0, 1000)).IsEqualTo(0);
        await Assert.That(ExplodeBuffRules.DamageFor(-3, 1000)).IsEqualTo(0);
        await Assert.That(ExplodeBuffRules.DamageFor(5, 0)).IsEqualTo(0);
        await Assert.That(ExplodeBuffRules.DamageFor(5, -1)).IsEqualTo(0);
    }
}
