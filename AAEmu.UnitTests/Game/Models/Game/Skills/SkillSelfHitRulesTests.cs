using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

public class SkillSelfHitRulesTests
{
    private static SkillEffect Effect(EffectTemplate template) => new() { Template = template };

    [Test]
    public async Task GroundOrigin_DamageIsNotAppliedToTheCaster()
    {
        await Assert.That(SkillSelfHitRules.AllowsOriginFallbackTarget(
            isOriginFallbackOnly: true, casterObjId: 100, targetObjId: 100,
            Effect(new DamageEffect()))).IsFalse();
    }

    [Test]
    public async Task GroundOrigin_DebuffIsNotAppliedToTheCaster()
    {
        await Assert.That(SkillSelfHitRules.AllowsOriginFallbackTarget(
            isOriginFallbackOnly: true, casterObjId: 100, targetObjId: 100,
            Effect(new BuffEffect { Buff = new BuffTemplate { Kind = BuffKind.Bad } }))).IsFalse();
    }

    [Test]
    public async Task GroundOrigin_UtilityAndSelfBuffsStillReachTheOrigin()
    {
        // A ground cast carries the caster as its origin so spawn/doodad effects have a position, and
        // self-targeted good buffs cast at a position are still meant to land on the caster.
        await Assert.That(SkillSelfHitRules.AllowsOriginFallbackTarget(
            isOriginFallbackOnly: true, casterObjId: 100, targetObjId: 100,
            Effect(new BuffEffect { Buff = new BuffTemplate { Kind = BuffKind.Good } }))).IsTrue();
        await Assert.That(SkillSelfHitRules.AllowsOriginFallbackTarget(
            isOriginFallbackOnly: true, casterObjId: 100, targetObjId: 100,
            Effect(new SpawnEffect()))).IsTrue();
    }

    [Test]
    public async Task RealTargets_AreNeverFiltered()
    {
        await Assert.That(SkillSelfHitRules.AllowsOriginFallbackTarget(
            isOriginFallbackOnly: false, casterObjId: 100, targetObjId: 100,
            Effect(new DamageEffect()))).IsTrue();
        await Assert.That(SkillSelfHitRules.AllowsOriginFallbackTarget(
            isOriginFallbackOnly: true, casterObjId: 100, targetObjId: 200,
            Effect(new DamageEffect()))).IsTrue();
    }
}
