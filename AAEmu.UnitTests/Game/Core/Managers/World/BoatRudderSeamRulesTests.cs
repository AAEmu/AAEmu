using AAEmu.Game.Core.Managers.World;

namespace AAEmu.UnitTests.Game.Core.Managers.World;

public class BoatRudderSeamRulesTests
{
    [Test]
    public async Task PinnedZoneId_KeepsTheFirstStreamedId()
    {
        await Assert.That(BoatRudderSeamRules.PinnedZoneId(0, 186)).IsEqualTo((ushort)186);
        await Assert.That(BoatRudderSeamRules.PinnedZoneId(186, 218)).IsEqualTo((ushort)186);
        await Assert.That(BoatRudderSeamRules.PinnedZoneId(186, 186)).IsEqualTo((ushort)186);
        await Assert.That(BoatRudderSeamRules.PinnedZoneId(186, 149)).IsEqualTo((ushort)186);
    }

    [Test]
    public async Task RebasedTime_SameClockPassesThrough()
    {
        await Assert.That(BoatRudderSeamRules.RebasedTime(0, 800, 0, 62)).IsEqualTo((800u, 0u));
        await Assert.That(BoatRudderSeamRules.RebasedTime(180_000, 180_050, 0, 62)).IsEqualTo((180_050u, 0u));
    }

    [Test]
    public async Task RebasedTime_NewClockContinuesByRealTimeAndKeepsItsDeltas()
    {
        // Live 18:00:57: 218 streamed 120 753, 186's first body reads 2327. Old rule: 120 754,
        // 120 755, … one millisecond per 62 ms body for the next two minutes.
        var (t1, offset) = BoatRudderSeamRules.RebasedTime(120_753, 2327, 0, 62);
        await Assert.That(t1).IsEqualTo(120_815u);
        await Assert.That(offset).IsEqualTo(120_815u - 2327u);

        // Following bodies from the same new clock keep their own spacing (2386, 2448 → +59, +62).
        var (t2, offset2) = BoatRudderSeamRules.RebasedTime(t1, 2386, offset, 63);
        await Assert.That(t2).IsEqualTo(120_874u);
        await Assert.That(offset2).IsEqualTo(offset);
        var (t3, _) = BoatRudderSeamRules.RebasedTime(t2, 2448, offset2, 62);
        await Assert.That(t3).IsEqualTo(120_936u);
    }

    [Test]
    public async Task RebasedTime_ElapsedIsClampedToOneSecondAndAtLeastOneMs()
    {
        await Assert.That(BoatRudderSeamRules.RebasedTime(5000, 10, 0, 0).Time).IsEqualTo(5001u);
        await Assert.That(BoatRudderSeamRules.RebasedTime(5000, 10, 0, 30_000).Time).IsEqualTo(6000u);
    }

    [Test]
    public async Task StreamedSteering_HoldsLastWhenIncomingCentersUnderAHeldStick()
    {
        await Assert.That(BoatRudderSeamRules.StreamedSteering(127, 0, 127)).IsEqualTo((sbyte)127);
        await Assert.That(BoatRudderSeamRules.StreamedSteering(-80, -4, -127)).IsEqualTo((sbyte)(-80));
        await Assert.That(BoatRudderSeamRules.StreamedSteering(127, 90, 127)).IsEqualTo((sbyte)90);
    }

    [Test]
    public async Task StreamedSteering_FollowsIncomingWhenTheStickIsReleasedOrReversed()
    {
        await Assert.That(BoatRudderSeamRules.StreamedSteering(127, 0, 0)).IsEqualTo((sbyte)0);
        await Assert.That(BoatRudderSeamRules.StreamedSteering(127, -20, -127)).IsEqualTo((sbyte)(-20));
        await Assert.That(BoatRudderSeamRules.StreamedSteering(0, -6, -6)).IsEqualTo((sbyte)(-6));
    }

    [Test]
    public async Task Pin_FollowSwitchKeepsZoneAndTimeAndHeldRudder()
    {
        var last = new BoatRudderSeamRules.StreamedShipVisual(186, 180_000, 127);
        var pinned = BoatRudderSeamRules.Pin(last, 218, 900, 0, 127, elapsedMs: 62);
        await Assert.That(pinned.ZoneId).IsEqualTo((ushort)186);
        await Assert.That(pinned.Time).IsEqualTo(180_062u);
        await Assert.That(pinned.TimeOffset).IsEqualTo(180_062u - 900u);
        await Assert.That(pinned.Steering).IsEqualTo((sbyte)127);
    }
}
