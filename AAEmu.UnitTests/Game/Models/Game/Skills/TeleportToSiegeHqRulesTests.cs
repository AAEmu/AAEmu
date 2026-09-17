using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// teleport_to_siege_hq (type 65) sends the caster to its return district's resurrection point. Both shipped
/// skills are 진지로 이동 with all-zero rows, so the destination comes from the character and the effect's
/// only gate is that there is one and that the zone it sits in is simulated.
/// </summary>
public class TeleportToSiegeHqRulesTests
{
    private const uint RespawnPoint = 1234;
    private const uint Zone = 186;

    [Test]
    public async Task APlayerWithARespawnPointInALoadedZone_MayGo()
    {
        await Assert.That(TeleportToSiegeHqRules.CanTeleportTo(
            isPlayerCharacter: true, RespawnPoint, Zone,
            zoneAuthority: true, isZoneLoaded: _ => true)).IsTrue();
    }

    [Test]
    public async Task OnlyAPlayerCharacterIsMoved()
    {
        // A mob or a summon has no return district to be sent to.
        await Assert.That(TeleportToSiegeHqRules.CanTeleportTo(
            isPlayerCharacter: false, RespawnPoint, Zone,
            zoneAuthority: true, isZoneLoaded: _ => true)).IsFalse();
    }

    [Test]
    public async Task ADistrictWithNoRespawnPoint_IsRefused()
    {
        await Assert.That(TeleportToSiegeHqRules.CanTeleportTo(
            isPlayerCharacter: true, returnPointId: 0, Zone,
            zoneAuthority: true, isZoneLoaded: _ => true)).IsFalse();
    }

    [Test]
    public async Task AnUnloadedDestination_IsRefused()
    {
        // The character would be stranded in a zone nobody simulates.
        await Assert.That(TeleportToSiegeHqRules.CanTeleportTo(
            isPlayerCharacter: true, RespawnPoint, Zone,
            zoneAuthority: true, isZoneLoaded: _ => false)).IsFalse();
    }

    [Test]
    public async Task WithoutZoneAuthority_OrWithoutAProbe_NothingIsRefused()
    {
        await Assert.That(TeleportToSiegeHqRules.CanTeleportTo(
            true, RespawnPoint, Zone, zoneAuthority: false, isZoneLoaded: _ => false)).IsTrue();
        await Assert.That(TeleportToSiegeHqRules.CanTeleportTo(
            true, RespawnPoint, Zone, zoneAuthority: true, isZoneLoaded: null)).IsTrue();
    }
}
