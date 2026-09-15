using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// The rule behind a <c>buff_effects</c> row whose <c>buff_id</c> is not in <c>buffs</c>. 10.0.2.13 ships 41
/// of those rows and 19 enabled skills reach them (10580 → row 307, 11454 → 780, 28698-28700 → 15918-15920).
/// The row stays registered so the effect-id lookups that point at it still resolve, but it must never
/// apply, report or tick anything.
/// </summary>
public class BuffEffectDispatchRulesTests
{
    [Test]
    public async Task IsDispatchable_NoBuffTemplate_IsFalse()
    {
        await Assert.That(BuffEffectDispatchRules.IsDispatchable(null)).IsFalse();
    }

    [Test]
    public async Task IsDispatchable_BuffTemplate_IsTrue()
    {
        await Assert.That(BuffEffectDispatchRules.IsDispatchable(new BuffTemplate { Id = 232 })).IsTrue();
    }

    [Test]
    public async Task ResolveBuffId_NoBuffTemplate_IsZero()
    {
        // 0 is the id every consumer already filters on. The effect's own id (307) names no buff anywhere.
        await Assert.That(BuffEffectDispatchRules.ResolveBuffId(null)).IsEqualTo(0u);
    }

    [Test]
    public async Task ResolveBuffId_BuffTemplate_IsTheTemplateId()
    {
        await Assert.That(BuffEffectDispatchRules.ResolveBuffId(new BuffTemplate { Id = 232 })).IsEqualTo(232u);
    }

    [Test]
    public async Task HasTick_NoBuffTemplate_IsFalse()
    {
        await Assert.That(BuffEffectDispatchRules.HasTick(null)).IsFalse();
    }

    [Test]
    public async Task HasTick_TickingBuffTemplate_IsTrue()
    {
        await Assert.That(BuffEffectDispatchRules.HasTick(new BuffTemplate { Id = 232, Tick = 2000 })).IsTrue();
    }

    [Test]
    public async Task HasTick_NonTickingBuffTemplate_IsFalse()
    {
        await Assert.That(BuffEffectDispatchRules.HasTick(new BuffTemplate { Id = 232, Tick = 0 })).IsFalse();
    }
}
