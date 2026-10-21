using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

public class CombatResourceGrantRulesTests
{
    [Test]
    public async Task BuffTick_CeilingSizedGrant_IsOne()
    {
        // Fury tick CRE 472 stores +10 on a max-10 Hatred pool; tooltip is +1/sec after the +5 start.
        await Assert.That(CombatResourceGrantRules.ResolveBuffTickAmount(10, 10)).IsEqualTo(1);
        await Assert.That(CombatResourceGrantRules.ResolveBuffTickAmount(24, 24)).IsEqualTo(1);
    }

    [Test]
    public async Task BuffTick_OverCeilingGrant_IsOne()
    {
        // Assassin stealth stages tick +45/+90 into 살기 (max 10).
        await Assert.That(CombatResourceGrantRules.ResolveBuffTickAmount(45, 10)).IsEqualTo(1);
        await Assert.That(CombatResourceGrantRules.ResolveBuffTickAmount(90, 10)).IsEqualTo(1);
    }

    [Test]
    public async Task BuffTick_OrdinaryGrant_Unchanged()
    {
        await Assert.That(CombatResourceGrantRules.ResolveBuffTickAmount(1, 10)).IsEqualTo(1);
        await Assert.That(CombatResourceGrantRules.ResolveBuffTickAmount(5, 10)).IsEqualTo(5);
    }

    [Test]
    public async Task BuffTick_MaxOnePool_Unchanged()
    {
        // A max-1 pool whose tick stores 1 is a real +1, not a sentinel refill.
        await Assert.That(CombatResourceGrantRules.ResolveBuffTickAmount(1, 1)).IsEqualTo(1);
    }

    [Test]
    public async Task IsPeriodicBuffTick_CastBuffWithoutTriggerFlag_IsTrue()
    {
        // BuffTemplate.TimeToTimeApply builds EffectSource(buff) with FromBuffTrigger left false.
        var cast = new CastBuff(buff: null);
        var source = new EffectSource();
        await Assert.That(CombatResourceGrantRules.IsPeriodicBuffTick(cast, source)).IsTrue();
        await Assert.That(CombatResourceGrantRules.IsPeriodicBuffTick(cast, null)).IsTrue();
    }

    [Test]
    public async Task IsPeriodicBuffTick_FromBuffTrigger_IsFalse()
    {
        // BuffTrigger.Fire sets FromBuffTrigger; a ceiling-sized timeout/event grant must stay intact.
        var cast = new CastBuff(buff: null);
        var source = new EffectSource { FromBuffTrigger = true };
        await Assert.That(CombatResourceGrantRules.IsPeriodicBuffTick(cast, source)).IsFalse();
    }

    [Test]
    public async Task BuffTrigger_CeilingSizedGrant_StaysAuthored()
    {
        // Gate: only periodic ticks call ResolveBuffTickAmount; trigger path leaves the amount alone.
        var amount = 10;
        var resourceMax = 10;
        var fromTrigger = !CombatResourceGrantRules.IsPeriodicBuffTick(
            new CastBuff(buff: null),
            new EffectSource { FromBuffTrigger = true });
        await Assert.That(fromTrigger).IsTrue();
        await Assert.That(amount).IsEqualTo(10);
        await Assert.That(CombatResourceGrantRules.ResolveBuffTickAmount(amount, resourceMax)).IsEqualTo(1);
    }

    [Test]
    public async Task Plot_MismatchedEnhancedGrant_UsesSkillEffectsAmount()
    {
        // Mana Bolt under Fury: plot +4, skill_effects +1 (synergy icon "1회씩 누적").
        var effects = new List<SkillEffect>
        {
            CreEffect(resourceId: 15, amount: 1, sourceBuffTagId: 3744)
        };

        var resolved = CombatResourceGrantRules.ResolvePlotAmount(
            plotAmount: 4,
            resourceId: 15,
            skillOwnedResourceId: 0,
            effects,
            _ => [22982u],
            _ => true);

        await Assert.That(resolved).IsEqualTo(1);
    }

    [Test]
    public async Task Plot_MatchingGrant_KeepsPlotAmount()
    {
        var effects = new List<SkillEffect>
        {
            CreEffect(resourceId: 15, amount: 1, sourceBuffTagId: 3744)
        };

        var resolved = CombatResourceGrantRules.ResolvePlotAmount(
            plotAmount: 1,
            resourceId: 15,
            skillOwnedResourceId: 0,
            effects,
            _ => [22982u],
            _ => true);

        await Assert.That(resolved).IsEqualTo(1);
    }

    [Test]
    public async Task Plot_Spend_NeverOverridden()
    {
        var effects = new List<SkillEffect>
        {
            CreEffect(resourceId: 11, amount: 1, sourceBuffTagId: 0)
        };

        var resolved = CombatResourceGrantRules.ResolvePlotAmount(
            plotAmount: -15,
            resourceId: 11,
            skillOwnedResourceId: 0,
            effects,
            _ => [],
            _ => false);

        await Assert.That(resolved).IsEqualTo(-15);
    }

    [Test]
    public async Task Plot_TaggedGrantPreferred_WhenSeveralPass()
    {
        // Sage poem: skill_effects +1 (untagged) and +3 (tagged); plot +3 must keep 3 when the tag is up.
        var effects = new List<SkillEffect>
        {
            CreEffect(resourceId: 11, amount: 1, sourceBuffTagId: 0),
            CreEffect(resourceId: 11, amount: 3, sourceBuffTagId: 1898)
        };

        var resolved = CombatResourceGrantRules.ResolvePlotAmount(
            plotAmount: 5,
            resourceId: 11,
            skillOwnedResourceId: 0,
            effects,
            tag => tag == 1898 ? [1u] : [],
            ids => ids is { Count: > 0 });

        await Assert.That(resolved).IsEqualTo(3);
    }

    [Test]
    public async Task Plot_NoSkillEffectsGrant_KeepsPlotAmount()
    {
        var resolved = CombatResourceGrantRules.ResolvePlotAmount(
            plotAmount: 4,
            resourceId: 15,
            skillOwnedResourceId: 0,
            skillEffects: [],
            _ => [],
            _ => false);

        await Assert.That(resolved).IsEqualTo(4);
    }

    [Test]
    public async Task Plot_SourceBuffTagGate_SkipsClosedGrant()
    {
        var effects = new List<SkillEffect>
        {
            CreEffect(resourceId: 15, amount: 1, sourceBuffTagId: 3744)
        };

        // Tag not present → no passing skill_effects grant → plot amount stands.
        var resolved = CombatResourceGrantRules.ResolvePlotAmount(
            plotAmount: 4,
            resourceId: 15,
            skillOwnedResourceId: 0,
            effects,
            _ => [],
            _ => false);

        await Assert.That(resolved).IsEqualTo(4);
    }

    private static SkillEffect CreEffect(int resourceId, int amount, uint sourceBuffTagId) =>
        new()
        {
            SourceBuffTagId = sourceBuffTagId,
            Template = new CombatResourceEffect
            {
                CombatResourceId = resourceId,
                MinCombatResource = amount,
                MaxCombatResource = amount,
                Chance = 100,
                ResetRemainTime = true
            }
        };
}
