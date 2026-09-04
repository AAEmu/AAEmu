using System.Numerics;

using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Slaves;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Movements;

namespace AAEmu.UnitTests.Game.Core.Managers.World;

public class ZoneCoordBoundaryTests
{
    [Test]
    public async Task ToZoneLocal_ZoneIdZero_DoesNotRewriteContinent()
    {
        var continent = new Vector3(15125.4f, 11437.2f, 172.1f);
        var got = ZoneCoordBoundary.ToZoneLocal(0, continent, force: true);
        await Assert.That(got).IsEqualTo(continent);
    }

    [Test]
    public async Task HelmRequest_IsNotTreatedAsASpatialPosition()
    {
        await Assert.That(ZoneCoordBoundary.CarriesSpatialPosition(new ShipRequestMoveType())).IsFalse();
        var actor = new UnitMoveType { X = 15125f, Y = 11437f, Z = 172f };
        await Assert.That(ZoneCoordBoundary.CarriesSpatialPosition(actor)).IsTrue();
        ZoneCoordBoundary.ShiftWorldToLocal(0, actor);
        await Assert.That(actor.X).IsEqualTo(15125f);
        await Assert.That(actor.Y).IsEqualTo(11437f);
    }

    [Test]
    public async Task UnsetEnv_DefaultsToContinentOnZoneWire()
    {
        await Assert.That(ZoneCoordBoundary.ResolveUseLocalOnZoneWire(null, null)).IsFalse();
        await Assert.That(ZoneCoordBoundary.ResolveUseLocalOnZoneWire("0", "1")).IsTrue();
        await Assert.That(ZoneCoordBoundary.ResolveUseLocalOnZoneWire(null, "1")).IsTrue();
    }

    [Test]
    public async Task WorldEquals1_ForcesContinentOnWire()
    {
        await Assert.That(ZoneCoordBoundary.ResolveUseLocalOnZoneWire("1", null)).IsFalse();
        await Assert.That(ZoneCoordBoundary.ResolveUseLocalOnZoneWire("1", "1")).IsFalse();
    }

    [Test]
    public async Task LocalEquals0_ForcesContinentOnWire()
    {
        await Assert.That(ZoneCoordBoundary.ResolveUseLocalOnZoneWire(null, "0")).IsFalse();
    }

    [Test]
    public async Task RetailLiveWire_IsContinentUnlessDebugEnv()
    {
        await Assert.That(ZoneCoordBoundary.ResolveUseLocalOnZoneWire(null, null)).IsFalse();
        await Assert.That(WzCoordPolicy.DebugRewriteLiveToZoneLocal)
            .IsEqualTo(ZoneCoordBoundary.UseLocalOnZoneWire);
    }

    [Test]
    public async Task UseWorldPositionOnWz_WhenARiderOrSailIsParented()
    {
        var hull = new Slave { Template = new SlaveTemplate { SlaveKind = SlaveKind.SmallSailingShip } };
        hull.Transform.Local.SetPosition(13078f, 9623f, 100f);
        var sail = new Slave { Template = new SlaveTemplate { SlaveKind = SlaveKind.SlaveEquipment } };
        sail.Transform.Parent = hull.Transform;
        sail.Transform.Local.SetPosition(0f, -14.7f, 6.2f);

        await Assert.That(WzCoordPolicy.UseWorldPositionOnWz(hull)).IsFalse();
        await Assert.That(WzCoordPolicy.UseWorldPositionOnWz(sail)).IsTrue();
        await Assert.That(sail.Transform.Local.Position.Y).IsEqualTo(-14.7f);
        await Assert.That(sail.Transform.World.Position.Y).IsNotEqualTo(-14.7f);
    }
}
