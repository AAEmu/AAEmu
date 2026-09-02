using AAEmu.Game.Core.Managers.World;

namespace AAEmu.UnitTests.Game.Core.Managers.World;

public class BoatHelmSeatRulesTests
{
    [Test]
    public async Task IsSeatedOnSlave_RequiresABindSeatAndASlaveParent()
    {
        await Assert.That(BoatHelmSeatRules.IsSeatedOnSlave(true, true)).IsTrue();
        await Assert.That(BoatHelmSeatRules.IsSeatedOnSlave(true, false)).IsFalse();
        await Assert.That(BoatHelmSeatRules.IsSeatedOnSlave(false, true)).IsFalse();
        await Assert.That(BoatHelmSeatRules.IsSeatedOnSlave(false, false)).IsFalse();
    }

    [Test]
    public async Task ShouldIgnoreActorMoveWhileSeated_FollowsTheSeat()
    {
        await Assert.That(BoatHelmSeatRules.ShouldIgnoreActorMoveWhileSeated(true)).IsTrue();
        await Assert.That(BoatHelmSeatRules.ShouldIgnoreActorMoveWhileSeated(false)).IsFalse();
    }

    [Test]
    public async Task ShouldForwardActorMoveToZone_IsTheInverseOfIgnore()
    {
        await Assert.That(BoatHelmSeatRules.ShouldForwardActorMoveToZone(true)).IsFalse();
        await Assert.That(BoatHelmSeatRules.ShouldForwardActorMoveToZone(false)).IsTrue();
    }

    [Test]
    public async Task ShouldKeepStreamedHullForRider_OnlyTheHullTheySitOn()
    {
        await Assert.That(BoatHelmSeatRules.ShouldKeepStreamedHullForRider(true)).IsTrue();
        await Assert.That(BoatHelmSeatRules.ShouldKeepStreamedHullForRider(false)).IsFalse();
    }

    [Test]
    public async Task ShouldRelayZoneModelPosture_SkipsASeatedRider()
    {
        await Assert.That(BoatHelmSeatRules.ShouldRelayZoneModelPosture(true)).IsFalse();
        await Assert.That(BoatHelmSeatRules.ShouldRelayZoneModelPosture(false)).IsTrue();
    }

    [Test]
    public async Task FollowSwitch_ReattachesTheRiderButDoesNotRebindTheHelm()
    {
        // Same hull objId on the client: a second SCSlaveBound + occupy buffs is three helm
        // rebind cycles at the seam, not a fix.
        await Assert.That(BoatHelmSeatRules.ShouldRebindHelmAtFollowSwitch).IsFalse();
    }
}
