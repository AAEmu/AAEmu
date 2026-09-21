using AAEmu.Game.Models.Game.Skills.Effects;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

public class KillNpcWithoutCorpseRulesTests
{
    [Test]
    public async Task RadiusMatch_RemovesOnlyTheNamedTemplate()
    {
        await Assert.That(KillNpcWithoutCorpseRules.IsVictim(
            effectNpcId: 20, vanish: true, unitTemplateId: 20,
            unitIsCaster: false, unitIsDead: false, inRadius: true)).IsTrue();
        await Assert.That(KillNpcWithoutCorpseRules.IsVictim(
            effectNpcId: 20, vanish: true, unitTemplateId: 10,
            unitIsCaster: false, unitIsDead: false, inRadius: true)).IsFalse();
    }

    [Test]
    public async Task Vanish_DoesNotDeleteADifferentTemplateCaster()
    {
        await Assert.That(KillNpcWithoutCorpseRules.IsVictim(
            effectNpcId: 20, vanish: true, unitTemplateId: 10,
            unitIsCaster: true, unitIsDead: false, inRadius: true)).IsFalse();
        await Assert.That(KillNpcWithoutCorpseRules.IsVictim(
            effectNpcId: 20, vanish: true, unitTemplateId: 10,
            unitIsCaster: true, unitIsDead: false, inRadius: false)).IsFalse();
    }

    [Test]
    public async Task Vanish_RemovesTheMatchingCasterEvenOutsideTheBand()
    {
        await Assert.That(KillNpcWithoutCorpseRules.IsVictim(
            effectNpcId: 10, vanish: true, unitTemplateId: 10,
            unitIsCaster: true, unitIsDead: false, inRadius: false)).IsTrue();
    }

    [Test]
    public async Task UnsetTemplate_VanishIsSelfRemoveOnly()
    {
        await Assert.That(KillNpcWithoutCorpseRules.IsVictim(
            effectNpcId: 0, vanish: true, unitTemplateId: 10,
            unitIsCaster: true, unitIsDead: false, inRadius: false)).IsTrue();
        await Assert.That(KillNpcWithoutCorpseRules.IsVictim(
            effectNpcId: 0, vanish: true, unitTemplateId: 20,
            unitIsCaster: false, unitIsDead: false, inRadius: true)).IsFalse();
        await Assert.That(KillNpcWithoutCorpseRules.IsVictim(
            effectNpcId: 0, vanish: false, unitTemplateId: 10,
            unitIsCaster: true, unitIsDead: false, inRadius: true)).IsFalse();
    }

    [Test]
    public async Task DeadUnits_AreNotRemoved()
    {
        await Assert.That(KillNpcWithoutCorpseRules.IsVictim(
            effectNpcId: 10, vanish: true, unitTemplateId: 10,
            unitIsCaster: true, unitIsDead: true, inRadius: true)).IsFalse();
    }

    [Test]
    public async Task ExplicitTarget_CountsWhenTheRadiusSearchCannotSeeIt()
    {
        // A radius of 0 returns no neighbours, and the search leaves out its origin, so the named
        // NPC the skill was aimed at is still a victim. A different template is not.
        await Assert.That(KillNpcWithoutCorpseRules.IsVictim(
            effectNpcId: 20, vanish: false, unitTemplateId: 20,
            unitIsCaster: false, unitIsDead: false, inRadius: false, isExplicitTarget: true)).IsTrue();
        await Assert.That(KillNpcWithoutCorpseRules.IsVictim(
            effectNpcId: 20, vanish: false, unitTemplateId: 21,
            unitIsCaster: false, unitIsDead: false, inRadius: false, isExplicitTarget: true)).IsFalse();
        await Assert.That(KillNpcWithoutCorpseRules.IsVictim(
            effectNpcId: 0, vanish: true, unitTemplateId: 20,
            unitIsCaster: false, unitIsDead: false, inRadius: false, isExplicitTarget: true)).IsFalse();
    }

    [Test]
    public async Task OutsideTheBand_ANonCasterIsLeftAlone()
    {
        await Assert.That(KillNpcWithoutCorpseRules.IsVictim(
            effectNpcId: 10, vanish: false, unitTemplateId: 10,
            unitIsCaster: true, unitIsDead: false, inRadius: false)).IsFalse();
        await Assert.That(KillNpcWithoutCorpseRules.IsVictim(
            effectNpcId: 10, vanish: true, unitTemplateId: 10,
            unitIsCaster: false, unitIsDead: false, inRadius: false)).IsFalse();
    }
}
