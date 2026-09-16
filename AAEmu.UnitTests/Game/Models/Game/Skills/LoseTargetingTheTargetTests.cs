using AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;
using AAEmu.Game.Models.Game.Units;

using WorldIntegration = AAEmu.Game.WorldIntegration;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// lose_targeting_the_target (type 146) clears the current target of the unit the effect landed on, which is
/// the warper itself on 미러 워프 23936/23937 (target_type_id 0, single-target area) and the buff owner on the
/// 42 trigger rows. Under zone authority the clear is relayed and the World mirror waits for the zone.
/// </summary>
[NotInParallel]
public class LoseTargetingTheTargetTests
{
    private bool _previousZoneAuthority;
    private Action<uint, uint, bool> _previousRelay;

    [Before(Test)]
    public void Setup()
    {
        _previousZoneAuthority = WorldIntegration.ZoneAuthority;
        _previousRelay = WorldIntegration.RelayTargetChangedToZone;
    }

    [After(Test)]
    public void Teardown()
    {
        WorldIntegration.ZoneAuthority = _previousZoneAuthority;
        WorldIntegration.RelayTargetChangedToZone = _previousRelay;
    }

    [Test]
    public async Task WithoutZoneAuthority_TheTargetIsClearedLocally()
    {
        WorldIntegration.ZoneAuthority = false;
        var affected = new Unit { ObjId = 42 };
        affected.CurrentTarget = new Unit { ObjId = 7 };

        RunEffect(affected);

        await Assert.That(affected.CurrentTarget).IsNull();
    }

    [Test]
    public async Task UnderZoneAuthority_TheClearIsRelayedAndTheMirrorIsLeftAlone()
    {
        WorldIntegration.ZoneAuthority = true;
        (uint ObjId, uint Target, bool ForceByWorld)? relayed = null;
        WorldIntegration.RelayTargetChangedToZone = (objId, target, forceByWorld) =>
            relayed = (objId, target, forceByWorld);

        var affected = new Unit { ObjId = 42 };
        var tracked = new Unit { ObjId = 7 };
        affected.CurrentTarget = tracked;

        RunEffect(affected);

        await Assert.That(relayed).IsNotNull();
        await Assert.That(relayed!.Value.ObjId).IsEqualTo(42u);
        await Assert.That(relayed.Value.Target).IsEqualTo(0u); // the native clear-target sentinel
        await Assert.That(relayed.Value.ForceByWorld).IsTrue();
        await Assert.That(affected.CurrentTarget).IsSameReferenceAs(tracked);
    }

    [Test]
    public async Task WithoutZoneAuthority_NothingIsRelayed()
    {
        WorldIntegration.ZoneAuthority = false;
        var relayed = false;
        WorldIntegration.RelayTargetChangedToZone = (_, _, _) => relayed = true;
        var affected = new Unit { ObjId = 42 };

        RunEffect(affected);

        await Assert.That(relayed).IsFalse();
    }

    private static void RunEffect(BaseUnit target) =>
        new LoseTargetingTheTarget().Execute(
            caster: null, casterObj: null, target, targetObj: null, castObj: null,
            skill: null, skillObject: null, time: DateTime.UtcNow,
            value1: 4, value2: 0, value3: 0, value4: 0);
}
