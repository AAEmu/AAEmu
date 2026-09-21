using AAEmu.Game.Models.Game.NPChar;

namespace AAEmu.UnitTests.Game.Models.Game.NPChar;

public class NpcDespawnAckRulesTests
{
    [Test]
    public async Task ZoneCreatedMirror_WaitsForTheZoneAck()
    {
        await Assert.That(NpcDespawnAckRules.WaitForZoneRemoveAck(
            zoneAuthority: true, isWorldAuthored: false)).IsTrue();
    }

    [Test]
    public async Task WorldAuthoredUnit_IsRetiredImmediately()
    {
        await Assert.That(NpcDespawnAckRules.WaitForZoneRemoveAck(
            zoneAuthority: true, isWorldAuthored: true)).IsFalse();
    }

    [Test]
    public async Task WithoutZoneAuthority_NothingWaitsForAZoneAck()
    {
        await Assert.That(NpcDespawnAckRules.WaitForZoneRemoveAck(
            zoneAuthority: false, isWorldAuthored: false)).IsFalse();
        await Assert.That(NpcDespawnAckRules.WaitForZoneRemoveAck(
            zoneAuthority: false, isWorldAuthored: true)).IsFalse();
    }
}
