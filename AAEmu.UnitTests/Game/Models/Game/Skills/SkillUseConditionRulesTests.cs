using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Static;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

public class SkillUseConditionRulesTests
{
    private static SkillUseConditionRules.CasterState State(
        bool dead = false, bool stunned = false, bool asleep = false, bool silenced = false, bool swimming = false)
        => new(dead, stunned, asleep, silenced, swimming);

    private const long AliveOnly = 1L << (SkillUseConditionRules.SourceAlive - 1);
    private const long DeadOnly = 1L << (SkillUseConditionRules.SourceDead - 1);
    private const long StunAllowed = AliveOnly | (1L << (SkillUseConditionRules.SourceStun - 1));
    private const long SleepAndSilenceAllowed = AliveOnly
        | (1L << (SkillUseConditionRules.SourceSleep - 1))
        | (1L << (SkillUseConditionRules.SourceSilence - 1));

    [Test]
    public async Task AliveCaster_WithTheDefaultBits_Casts()
    {
        await Assert.That(SkillUseConditionRules.Evaluate(AliveOnly, State())).IsNull();
    }

    [Test]
    public async Task AliveCaster_WithoutSourceAlive_IsRejected()
    {
        // 742 rows carry source_dead and nothing else: they are death-triggered skills.
        await Assert.That(SkillUseConditionRules.Evaluate(DeadOnly, State(dead: false)))
            .IsEqualTo(SkillResult.SourceAlive);
    }

    [Test]
    public async Task DeadCaster_NeedsSourceDead()
    {
        await Assert.That(SkillUseConditionRules.Evaluate(AliveOnly, State(dead: true)))
            .IsEqualTo(SkillResult.SourceDied);
        // A death-triggered skill runs for the corpse.
        await Assert.That(SkillUseConditionRules.Evaluate(DeadOnly, State(dead: true))).IsNull();
    }

    [Test]
    public async Task SilencedCaster_CannotCastAnOrdinarySpell()
    {
        await Assert.That(SkillUseConditionRules.Evaluate(AliveOnly, State(silenced: true)))
            .IsEqualTo(SkillResult.Silence);
    }

    [Test]
    public async Task SilencedCaster_CanCastASkillThatPermitsIt()
    {
        // 20513 = alive + stun + sleep + silence: 강인한 의지, 활력 방패, 기선 제압.
        await Assert.That(SkillUseConditionRules.Evaluate(SleepAndSilenceAllowed, State(asleep: true, silenced: true)))
            .IsNull();
    }

    [Test]
    public async Task StunnedCaster_IsBlockedUnlessTheSkillPermitsIt()
    {
        await Assert.That(SkillUseConditionRules.Evaluate(AliveOnly, State(stunned: true)))
            .IsEqualTo(SkillResult.CannotCastInStun);
        await Assert.That(SkillUseConditionRules.Evaluate(StunAllowed, State(stunned: true))).IsNull();
    }

    [Test]
    public async Task SleepingCaster_IsBlockedUnlessTheSkillPermitsIt()
    {
        // Sleep reuses the stun result: the client's SkillResult table has no sleep entry.
        await Assert.That(SkillUseConditionRules.Evaluate(AliveOnly, State(asleep: true)))
            .IsEqualTo(SkillResult.CannotCastInStun);
        await Assert.That(SkillUseConditionRules.Evaluate(SleepAndSilenceAllowed, State(asleep: true))).IsNull();
    }

    [Test]
    public async Task SwimmingCaster_IsBlockedBySourceNotSwim_AndRequiredBySourceShouldSwim()
    {
        var notSwim = AliveOnly | (1L << (SkillUseConditionRules.SourceNotSwim - 1));
        var shouldSwim = AliveOnly | (1L << (SkillUseConditionRules.SourceShouldSwim - 1));

        await Assert.That(SkillUseConditionRules.Evaluate(notSwim, State(swimming: true)))
            .IsEqualTo(SkillResult.CannotCastInSwimming);
        await Assert.That(SkillUseConditionRules.Evaluate(notSwim, State(swimming: false))).IsNull();
        await Assert.That(SkillUseConditionRules.Evaluate(shouldSwim, State(swimming: false)))
            .IsEqualTo(SkillResult.OnlyDuringSwimming);
        await Assert.That(SkillUseConditionRules.Evaluate(shouldSwim, State(swimming: true))).IsNull();
    }

    [Test]
    public async Task RootedCaster_IsNotBlocked()
    {
        // crippled (14) is a root: a rooted character still casts, so it is not part of the state.
        var crippled = AliveOnly | (1L << (SkillUseConditionRules.SourceCrippled - 1));
        await Assert.That(SkillUseConditionRules.Evaluate(crippled, State())).IsNull();
    }

    [Test]
    public async Task MountBits_AreNotEnforced()
    {
        // 3,330 mount skills carry 1|3, but 28735 sets 2|3|4|5|6|7 at once, so the bits cannot all be
        // requirements. Nothing is blocked either way until a client pass settles which is which.
        var mount = AliveOnly | (1L << (SkillUseConditionRules.SourceMount - 1));
        var anyState = 0b1111111L | (1L << 12) | (1L << 13) | (1L << 14);
        await Assert.That(SkillUseConditionRules.Evaluate(mount, State())).IsNull();
        await Assert.That(SkillUseConditionRules.Evaluate(anyState, State(dead: true))).IsNull();
        await Assert.That(SkillUseConditionRules.Evaluate(anyState, State(stunned: true, silenced: true))).IsNull();
    }

    [Test]
    public async Task UnlearnedCast_AllowsEverythingButAbilitySkills()
    {
        // Basic attacks, racial defaults, common skills and item casts are not learned.
        await Assert.That(SkillUseConditionRules.AllowsUnlearnedCast(AbilityType.General, false, false, false)).IsTrue();
        await Assert.That(SkillUseConditionRules.AllowsUnlearnedCast(AbilityType.Fight, true, false, false)).IsTrue();
        await Assert.That(SkillUseConditionRules.AllowsUnlearnedCast(AbilityType.Fight, false, true, false)).IsTrue();
        await Assert.That(SkillUseConditionRules.AllowsUnlearnedCast(AbilityType.Fight, false, false, true)).IsTrue();
        // A real ability skill is not.
        await Assert.That(SkillUseConditionRules.AllowsUnlearnedCast(AbilityType.Fight, false, false, false)).IsFalse();
    }

    [Test]
    public async Task UnlearnedSkillResult_IsTheTrainedSkillError()
    {
        await Assert.That(SkillUseConditionRules.UnlearnedSkillResult).IsEqualTo(SkillResult.UrkTrainedSkill);
    }

    [Test]
    public async Task TargetAliveDead_MatchesThePlotFilter()
    {
        // Default (alive 't', dead 'f'): corpses are not valid targets.
        await Assert.That(SkillUseConditionRules.AllowsTarget(true, false, targetIsDead: false)).IsTrue();
        await Assert.That(SkillUseConditionRules.AllowsTarget(true, false, targetIsDead: true)).IsFalse();
        // alive 'f' means only corpses qualify.
        await Assert.That(SkillUseConditionRules.AllowsTarget(false, false, targetIsDead: true)).IsTrue();
        await Assert.That(SkillUseConditionRules.AllowsTarget(false, false, targetIsDead: false)).IsFalse();
        // Both set: either is accepted.
        await Assert.That(SkillUseConditionRules.AllowsTarget(true, true, targetIsDead: true)).IsTrue();
        await Assert.That(SkillUseConditionRules.AllowsTarget(true, true, targetIsDead: false)).IsTrue();
    }
}
