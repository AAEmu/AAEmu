using AAEmu.Game.Core.Managers.World;

namespace AAEmu.UnitTests.Game.Core.Managers.World;

public class BoatZoneHostGateTests
{
    private const uint Continent = 0;
    private const uint ZoneId = 149;

    [Test]
    public async Task HasHost_WithoutProbes_AssumesHosted()
    {
        await Assert.That(BoatZoneHostGate.HasHost(ZoneId, Continent, null, null)).IsTrue();
    }

    [Test]
    public async Task HasHost_FollowsContinentProbe()
    {
        await Assert.That(BoatZoneHostGate.HasHost(ZoneId, Continent, _ => true, null)).IsTrue();
        await Assert.That(BoatZoneHostGate.HasHost(ZoneId, Continent, _ => false, null)).IsFalse();
    }

    [Test]
    public async Task HasHost_InsideACopy_UsesTheCopyProbe()
    {
        const uint instanceId = 3;

        // The continent probe answers for the zone key alone and would report the wrong copy.
        await Assert
            .That(BoatZoneHostGate.HasHost(ZoneId, instanceId, _ => true, (_, _) => false))
            .IsFalse();
        await Assert
            .That(BoatZoneHostGate.HasHost(ZoneId, instanceId, _ => false, (_, _) => true))
            .IsTrue();
    }

    [Test]
    public async Task HasHost_InsideACopyWithoutCopyProbe_FallsBackToTheZoneProbe()
    {
        await Assert.That(BoatZoneHostGate.HasHost(ZoneId, 3, _ => false, null)).IsFalse();
    }

    [Test]
    public async Task HasHost_UnknownZoneKey_IsNeverRefused()
    {
        await Assert.That(BoatZoneHostGate.HasHost(0, Continent, _ => false, (_, _) => false)).IsTrue();
    }

    [Test]
    public async Task HasHost_PassesTheZoneAndCopyToTheProbes()
    {
        uint probedZone = 0;
        uint probedInstance = 0;

        BoatZoneHostGate.HasHost(ZoneId, 7, _ => true, (zone, instance) =>
        {
            probedZone = zone;
            probedInstance = instance;
            return true;
        });

        await Assert.That(probedZone).IsEqualTo(ZoneId);
        await Assert.That(probedInstance).IsEqualTo(7u);
    }
}
