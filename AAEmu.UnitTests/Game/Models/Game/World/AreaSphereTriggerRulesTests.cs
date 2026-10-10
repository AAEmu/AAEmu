using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Spheres;

namespace AAEmu.UnitTests.Game.Models.Game.World;

/// <summary>
/// When an area sphere fires: on the edge it names, and no more often than its trigger condition lets it
/// in one world instance.
/// </summary>
public class AreaSphereTriggerRulesTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 5, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task AnUnfiredSphere_FiresUnderEveryCondition()
    {
        foreach (var condition in Enum.GetValues<AreaSphereTriggerCondition>())
            await Assert.That(AreaSphereTriggerRules.CanFire(condition, 3_600_000, null, Now)).IsTrue();
    }

    [Test]
    public async Task AOnceSphere_NeverFiresTwice()
    {
        var longAgo = Now.AddDays(-30);
        await Assert.That(AreaSphereTriggerRules.CanFire(AreaSphereTriggerCondition.TriggerOnceAtAll, 0, longAgo, Now)).IsFalse();
        await Assert.That(AreaSphereTriggerRules.CanFire(AreaSphereTriggerCondition.TriggerOnceInRuntime, 0, longAgo, Now)).IsFalse();
    }

    [Test]
    public async Task AnIntervalSphere_WaitsOutItsInterval()
    {
        const AreaSphereTriggerCondition every = AreaSphereTriggerCondition.TriggerEveryNTimeAfter;
        await Assert.That(AreaSphereTriggerRules.CanFire(every, 60_000, Now.AddMilliseconds(-59_999), Now)).IsFalse();
        await Assert.That(AreaSphereTriggerRules.CanFire(every, 60_000, Now.AddMilliseconds(-60_000), Now)).IsTrue();
    }

    [Test]
    public async Task AnIntervalOfZero_FiresEveryTime()
    {
        await Assert.That(AreaSphereTriggerRules.CanFire(
            AreaSphereTriggerCondition.TriggerEveryNTimeAfter, 0, Now, Now)).IsTrue();
    }

    [Test]
    public async Task ASphereFiresOnlyOnTheEdgeItNames()
    {
        await Assert.That(AreaSphereTriggerRules.FiresOn(enterOrLeave: true, entering: true)).IsTrue();
        await Assert.That(AreaSphereTriggerRules.FiresOn(enterOrLeave: true, entering: false)).IsFalse();
        await Assert.That(AreaSphereTriggerRules.FiresOn(enterOrLeave: false, entering: false)).IsTrue();
        await Assert.That(AreaSphereTriggerRules.FiresOn(enterOrLeave: false, entering: true)).IsFalse();
    }

    [Test]
    public async Task AClaimedSphere_StaysDormantForThatCharacter()
    {
        var sphere = IntervalSphere(3_600_000);
        var spheres = new SphereQuestManager(null);

        await Assert.That(spheres.TryClaimAreaSphereTrigger(sphere, Now, characterId: 8)).IsTrue();
        await Assert.That(spheres.TryClaimAreaSphereTrigger(sphere, Now.AddMinutes(59), characterId: 8)).IsFalse();
        await Assert.That(spheres.TryClaimAreaSphereTrigger(sphere, Now.AddHours(1), characterId: 8)).IsTrue();
    }

    [Test]
    public async Task TwoCharacters_EachGetTheirOwnClaim()
    {
        var sphere = IntervalSphere(3_600_000);
        var spheres = new SphereQuestManager(null);

        await Assert.That(spheres.TryClaimAreaSphereTrigger(sphere, Now, characterId: 8)).IsTrue();
        await Assert.That(spheres.TryClaimAreaSphereTrigger(sphere, Now, characterId: 39)).IsTrue();
        await Assert.That(spheres.TryClaimAreaSphereTrigger(sphere, Now.AddSeconds(1), characterId: 8)).IsFalse();
    }

    [Test]
    public async Task EachWorldInstance_HasItsOwnTrigger()
    {
        var sphere = IntervalSphere(3_600_000);

        await Assert.That(new SphereQuestManager(null).TryClaimAreaSphereTrigger(sphere, Now, 8)).IsTrue();
        await Assert.That(new SphereQuestManager(null).TryClaimAreaSphereTrigger(sphere, Now, 8)).IsTrue();
    }

    [Test]
    public async Task AReleasedClaim_LeavesTheSphereReady()
    {
        var sphere = IntervalSphere(3_600_000);
        var spheres = new SphereQuestManager(null);

        await Assert.That(spheres.TryClaimAreaSphereTrigger(sphere, Now, 8)).IsTrue();
        spheres.ReleaseAreaSphereTrigger(sphere, Now, 8);
        await Assert.That(spheres.TryClaimAreaSphereTrigger(sphere, Now.AddSeconds(1), 8)).IsTrue();
    }

    [Test]
    public async Task ReleasingAStaleClaim_KeepsTheNewerOne()
    {
        var sphere = IntervalSphere(3_600_000);
        var spheres = new SphereQuestManager(null);

        await Assert.That(spheres.TryClaimAreaSphereTrigger(sphere, Now, 8)).IsTrue();
        await Assert.That(spheres.TryClaimAreaSphereTrigger(sphere, Now.AddHours(1), 8)).IsTrue();
        spheres.ReleaseAreaSphereTrigger(sphere, Now, 8);
        await Assert.That(spheres.TryClaimAreaSphereTrigger(sphere, Now.AddHours(1).AddMinutes(1), 8)).IsFalse();
    }

    [Test]
    public async Task ZeroCharacterId_NeverClaims()
    {
        var sphere = IntervalSphere(3_600_000);
        await Assert.That(new SphereQuestManager(null).TryClaimAreaSphereTrigger(sphere, Now, 0)).IsFalse();
    }

    private static Spheres IntervalSphere(uint intervalMs) => new()
    {
        Id = 1,
        EnterOrLeave = true,
        TriggerConditionId = AreaSphereTriggerCondition.TriggerEveryNTimeAfter,
        TriggerConditionTime = intervalMs
    };
}
