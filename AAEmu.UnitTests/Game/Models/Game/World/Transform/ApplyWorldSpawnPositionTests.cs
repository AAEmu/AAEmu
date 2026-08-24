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
}
