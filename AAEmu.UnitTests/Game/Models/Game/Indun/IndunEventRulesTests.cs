using AAEmu.Game.Models.Game.Indun;

namespace AAEmu.UnitTests.Game.Models.Game.Indun;

public class IndunEventRulesTests
{
    [Test]
    public async Task ShouldFireKill_OnlyTheFirstRaisePerObject()
    {
        var fired = new HashSet<uint>();
        await Assert.That(IndunEventRules.ShouldFireKill(fired, 1001)).IsTrue();
        // The DoDie raise of the same death.
        await Assert.That(IndunEventRules.ShouldFireKill(fired, 1001)).IsFalse();
        await Assert.That(IndunEventRules.ShouldFireKill(fired, 1002)).IsTrue();
    }

    [Test]
    public async Task ShouldFireKill_AgainAfterTheObjectIsForgotten()
    {
        var fired = new HashSet<uint>();
        IndunEventRules.ShouldFireKill(fired, 1001);
        fired.Remove(1001);
        await Assert.That(IndunEventRules.ShouldFireKill(fired, 1001)).IsTrue();
    }

    [Test]
    public async Task ShouldFireCombatEnd_OncePerStart()
    {
        var inCombat = new HashSet<uint> { 1001 };
        await Assert.That(IndunEventRules.ShouldFireCombatEnd(inCombat, 1001)).IsTrue();
        await Assert.That(IndunEventRules.ShouldFireCombatEnd(inCombat, 1001)).IsFalse();
    }

    [Test]
    public async Task ShouldFireCombatEnd_NeverWithoutAStart()
    {
        await Assert.That(IndunEventRules.ShouldFireCombatEnd(new HashSet<uint>(), 1001)).IsFalse();
    }

    [Test]
    public async Task ShouldFireNoInAggroList_WhenArmedAndNobodyFights()
    {
        await Assert.That(IndunEventRules.ShouldFireNoInAggroList(armed: true, anyTaggedNpcInCombat: false)).IsTrue();
    }

    [Test]
    public async Task ShouldFireNoInAggroList_NotWhileATaggedNpcFights()
    {
        await Assert.That(IndunEventRules.ShouldFireNoInAggroList(armed: true, anyTaggedNpcInCombat: true)).IsFalse();
    }

    [Test]
    public async Task ShouldFireNoInAggroList_NotBeforeAnyEngagement()
    {
        await Assert.That(IndunEventRules.ShouldFireNoInAggroList(armed: false, anyTaggedNpcInCombat: false)).IsFalse();
        await Assert.That(IndunEventRules.ShouldFireNoInAggroList(armed: false, anyTaggedNpcInCombat: true)).IsFalse();
    }

    [Test]
    public async Task DifficultInRange_BoundsAreInclusive()
    {
        // indun_event_difficult_changeds row 1: 0..12.
        await Assert.That(IndunEventRules.DifficultInRange(0, 0, 12)).IsTrue();
        await Assert.That(IndunEventRules.DifficultInRange(12, 0, 12)).IsTrue();
        await Assert.That(IndunEventRules.DifficultInRange(7, 0, 12)).IsTrue();
    }

    [Test]
    public async Task DifficultInRange_OutsideIsRejected()
    {
        await Assert.That(IndunEventRules.DifficultInRange(13, 0, 12)).IsFalse();
        await Assert.That(IndunEventRules.DifficultInRange(-1, 0, 12)).IsFalse();
    }
}
