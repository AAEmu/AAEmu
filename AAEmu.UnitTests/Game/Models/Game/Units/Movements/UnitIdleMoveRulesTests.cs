using AAEmu.Game.Models.Game.Units.Movements;

namespace AAEmu.UnitTests.Game.Models.Game.Units.Movements;

public class UnitIdleMoveRulesTests
{
    [Test]
    public async Task StationaryStand_AtKnownPose_IsSuppressed()
    {
        await Assert.That(UnitIdleMoveRules.ShouldSuppress(
            19628f, 28276f, 295f, 0, 0, 10,
            19628f, 28276f, 295f, 0, 0, 10,
            0, 0, 0, 0, 0, 0, true)).IsTrue();
    }

    [Test]
    public async Task QuantizedNoise_InsideBand_IsSuppressed()
    {
        await Assert.That(UnitIdleMoveRules.ShouldSuppress(
            19628f, 28276f, 295f, 0, 0, 10,
            19628.1f, 28276.05f, 295.02f, 0, 0, 10,
            0, 0, 0, 0, 0, 0, true)).IsTrue();
    }

    [Test]
    public async Task WrappedHeading_IsStillStationary()
    {
        // 85 and -42 are one step apart on the 128-step heading circle.
        await Assert.That(UnitIdleMoveRules.ShouldSuppress(
            19628f, 28276f, 295f, 85, 0, 10,
            19628f, 28276f, 295f, -42, 0, 10,
            0, 0, 0, 0, 0, 0, true)).IsTrue();
    }

    [Test]
    public async Task OppositeHeading_IsRelayed()
    {
        await Assert.That(UnitIdleMoveRules.ShouldSuppress(
            19628f, 28276f, 295f, 0, 0, 10,
            19628f, 28276f, 295f, 64, 0, 10,
            0, 0, 0, 0, 0, 0, true)).IsFalse();
    }

    [Test]
    public async Task WalkVelocity_IsRelayed()
    {
        await Assert.That(UnitIdleMoveRules.ShouldSuppress(
            19628f, 28276f, 295f, 0, 0, 10,
            19628f, 28276f, 295f, 0, 0, 10,
            40, 0, 0, 0, 0, 0, true)).IsFalse();
    }

    [Test]
    public async Task RealStep_IsRelayed()
    {
        await Assert.That(UnitIdleMoveRules.ShouldSuppress(
            19628f, 28276f, 295f, 0, 0, 10,
            19629f, 28276f, 295f, 0, 0, 10,
            0, 0, 0, 0, 0, 0, true)).IsFalse();
    }

    [Test]
    public async Task TurnInPlace_IsRelayed()
    {
        await Assert.That(UnitIdleMoveRules.ShouldSuppress(
            19628f, 28276f, 295f, 0, 0, 10,
            19628f, 28276f, 295f, 0, 0, 40,
            0, 0, 0, 0, 0, 0, true)).IsFalse();
    }

    [Test]
    public async Task SectorLine_DoesNotSuppressARealCrossing()
    {
        // 28288 is a 64 m region edge. A metre of travel must stay live.
        await Assert.That(UnitIdleMoveRules.ShouldSuppress(
            19660f, 28287.6f, 295f, 0, 0, 0,
            19660f, 28288.8f, 295f, 0, 0, 0,
            0, 0, 0, 0, 0, 0, true)).IsFalse();
    }

    [Test]
    public async Task StandAfterMotion_InsideTheDuplicateBand_IsRelayed()
    {
        // The zone's halt: 5.7 cm and the same heading past the last moving record. Only the fact that
        // clients were last told "moving" keeps it out of the duplicate band.
        await Assert.That(UnitIdleMoveRules.ShouldSuppress(
            19628f, 28276f, 295f, 0, 0, 10,
            19628.05f, 28276.02f, 295f, 0, 0, 10,
            0, 0, 0, 0, 0, 0, false)).IsFalse();
    }

    [Test]
    public async Task RepeatStand_IsSuppressedOnlyAfterARelayedStand()
    {
        await Assert.That(UnitIdleMoveRules.ShouldSuppress(
            19628f, 28276f, 295f, 0, 0, 10,
            19628f, 28276f, 295f, 0, 0, 10,
            0, 0, 0, 0, 0, 0, false)).IsFalse();

        await Assert.That(UnitIdleMoveRules.ShouldSuppress(
            19628f, 28276f, 295f, 0, 0, 10,
            19628f, 28276f, 295f, 0, 0, 10,
            0, 0, 0, 0, 0, 0, true)).IsTrue();
    }

    [Test]
    public async Task MovingRecord_IsRelayedWhateverClientsLastHeard()
    {
        foreach (var lastRelayedWasStationary in new[] { true, false })
        {
            await Assert.That(UnitIdleMoveRules.ShouldSuppress(
                19628f, 28276f, 295f, 0, 0, 10,
                19628f, 28276f, 295f, 0, 0, 10,
                250, 0, 0, 0, 40, 0, lastRelayedWasStationary)).IsFalse();
        }
    }

    [Test]
    public async Task OffGroundHover_IsNeverSuppressed()
    {
        // Hellgate / hawk / shark: the hover stand is the client's altitude keepalive.
        await Assert.That(UnitIdleMoveRules.ShouldSuppress(
            19628f, 28276f, 153.4f, 0, 0, 0,
            19628f, 28276f, 153.4f, 0, 0, 0,
            0, 0, 0, 0, 0, 0, lastRelayedWasStationary: true, holdsAltitude: true)).IsFalse();
    }

    [Test]
    public async Task GroundRepeatStand_StaysSuppressedWhenNotOffGround()
    {
        // Same hover pose, but a walker. Plaza filter must still fire.
        await Assert.That(UnitIdleMoveRules.ShouldSuppress(
            19628f, 28276f, 153.4f, 0, 0, 0,
            19628f, 28276f, 153.4f, 0, 0, 0,
            0, 0, 0, 0, 0, 0, lastRelayedWasStationary: true, holdsAltitude: false)).IsTrue();
    }

    [Test]
    public async Task OffGround_DoesNotBypassARealStep()
    {
        await Assert.That(UnitIdleMoveRules.ShouldSuppress(
            19628f, 28276f, 153.4f, 0, 0, 0,
            19629f, 28276f, 153.4f, 0, 0, 0,
            0, 0, 0, 0, 0, 0, lastRelayedWasStationary: true, holdsAltitude: true)).IsFalse();
    }

    [Test]
    public async Task PlazaStand_UnchangedForGroundUnits()
    {
        await Assert.That(UnitIdleMoveRules.ShouldSuppress(
            19628f, 28276f, 295f, 0, 0, 10,
            19628f, 28276f, 295f, 0, 0, 10,
            0, 0, 0, 0, 0, 0, true, holdsAltitude: false)).IsTrue();
        await Assert.That(UnitIdleMoveRules.ShouldSuppress(
            19628f, 28276f, 295f, 0, 0, 10,
            19628f, 28276f, 295f, 0, 0, 10,
            0, 0, 0, 0, 0, 0, true, holdsAltitude: true)).IsFalse();
    }
}
