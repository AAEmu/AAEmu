using System.Numerics;

using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.World.Transform;

namespace AAEmu.UnitTests.Game.Models.Game.World.Transform;

public class ApplyWorldSpawnPositionTests
{
    private sealed class ZoneChangeProbe : GameObject
    {
        public Vector3 PositionWhenZoneChanged { get; private set; }

        public override void OnZoneChange(uint lastZoneKey, uint newZoneKey)
        {
            PositionWhenZoneChanged = Transform.World.Position;
        }
    }

    [Test]
    public async Task ApplyWorldSpawnPosition_OnZoneChangeSeesSpawnXyzNotPreviousContinent()
    {
        var probe = new ZoneChangeProbe();
        probe.Transform.ZoneId = 248;
        probe.Transform.Local.Position = new Vector3(7793.18f, 10322.50f, 249.29f);

        probe.Transform.ApplyWorldSpawnPosition(new WorldSpawnPosition
        {
            ZoneId = 265,
            X = 693.70f,
            Y = 727.30f,
            Z = 185.40f
        });

        await Assert.That(probe.Transform.ZoneId).IsEqualTo(265u);
        await Assert.That(probe.PositionWhenZoneChanged.X).IsEqualTo(693.70f);
        await Assert.That(probe.PositionWhenZoneChanged.Y).IsEqualTo(727.30f);
        await Assert.That(probe.PositionWhenZoneChanged.Z).IsEqualTo(185.40f);
    }

    /// <summary>
    /// A dungeon template spawn carries no zone of its own (its ZoneId is 0), so an enter built on the
    /// plain spawn position left the character's zone unresolved and the zone-authority World bounced
    /// them back to character select. The instance enter must take the zone and the copy from the
    /// instance, not from the spawn.
    /// </summary>
    [Test]
    public async Task ApplyInstanceSpawnPosition_TakesTheZoneFromTheInstanceNotTheSpawn()
    {
        var probe = new ZoneChangeProbe();
        probe.Transform.ZoneId = 0;

        // The instance id is passed through unchanged so the fixture never resolves WorldManager; the
        // defect under test is the zone: a dungeon template's spawn carries ZoneId 0, so the plain
        // spawn path left the character's zone unset and the zone-authority World could not route them
        // into the copy - they were returned to character select.
        var existingInstance = probe.Transform.InstanceId;
        probe.Transform.ApplyInstanceSpawnPosition(
            new WorldSpawnPosition { ZoneId = 0, X = 519.0f, Y = 654.5f, Z = 125.3f }, 373u, existingInstance);

        await Assert.That(probe.Transform.ZoneId).IsEqualTo(373u);
        await Assert.That(probe.Transform.InstanceId).IsEqualTo(existingInstance);
        await Assert.That(probe.Transform.Local.Position.X).IsEqualTo(519.0f);
        await Assert.That(probe.PositionWhenZoneChanged.X).IsEqualTo(519.0f);
    }
}
