using AAEmu.Game.Models.Game.World;

namespace AAEmu.UnitTests.Game.Models.Game.World;

/// <summary>
/// Who may move a unit. A World-authored move of a unit the dedicate simulates is a second driver:
/// World walks it, the zone streams its own position, and the unit ends up shuttling between the two.
/// </summary>
public class ZoneOwnedUnitRulesTests
{
    [Test]
    public async Task AMirroredNpcUnderZoneAuthority_IsTheZones()
    {
        await Assert.That(ZoneOwnedUnitRules.IsDrivenByZone(
            zoneAuthority: true, isZoneMirrorNpc: true)).IsTrue();
    }

    [Test]
    public async Task WithoutZoneAuthority_WorldStillDrivesEverything()
    {
        await Assert.That(ZoneOwnedUnitRules.IsDrivenByZone(
            zoneAuthority: false, isZoneMirrorNpc: true)).IsFalse();
    }

    [Test]
    public async Task ANpcNothingPublished_IsWorldsToMove()
    {
        // A pet, a summon or a spawn from before the zone took over: no zone simulates it, so a
        // World-authored move is the only driver it has.
        await Assert.That(ZoneOwnedUnitRules.IsDrivenByZone(
            zoneAuthority: true, isZoneMirrorNpc: false)).IsFalse();
    }
}
