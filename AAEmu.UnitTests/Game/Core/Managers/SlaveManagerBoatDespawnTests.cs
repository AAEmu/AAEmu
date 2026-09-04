using AAEmu.Game;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Core.Managers;

[NotInParallel]
public class SlaveManagerBoatDespawnTests
{
    [Test]
    public async Task DropHullFromZone_RemovesChildrenAndDoodadsBeforeTheHull()
    {
        var units = new List<uint>();
        var doodads = new List<uint>();
        HookRelays(units, doodads, out var restore);
        try
        {
            var hull = HullWithGrowlingKit();

            SlaveManager.DropHullFromZone(hull, 218);

            await Assert.That(units.Take(3)).IsEquivalentTo(new uint[] { 2346, 2348, 2349 });
            await Assert.That(units[^1]).IsEqualTo(2339u);
            await Assert.That(doodads).IsEquivalentTo(new uint[] { 2365 });
        }
        finally
        {
            restore();
        }
    }

    [Test]
    public async Task DropHullFromZone_UnknownZoneDoesNothing()
    {
        var units = new List<uint>();
        HookRelays(units, [], out var restore);
        try
        {
            SlaveManager.DropHullFromZone(HullWithGrowlingKit(), 0);
            await Assert.That(units).IsEmpty();
        }
        finally
        {
            restore();
        }
    }

    [Test]
    public async Task WithdrawBoatFromZone_AlsoDropsAChildsOwnZone()
    {
        var unitZones = new List<uint>();
        HookRelays([], [], out var restore, zone => unitZones.Add(zone));
        try
        {
            var hull = HullWithGrowlingKit();
            hull.ZoneAnnouncedTo = 186;
            // Assign without Transform.ZoneId so Unit.OnZoneChange does not require ZoneManager.
            typeof(AAEmu.Game.Models.Game.World.Transform.Transform)
                .GetField("_zoneId", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(hull.AttachedSlaves[0].Transform, 218u);

            SlaveManager.WithdrawBoatFromZone(hull);

            await Assert.That(unitZones.Distinct().OrderBy(z => z))
                .IsEquivalentTo(new uint[] { 186, 218 });
            await Assert.That(hull.ZoneAnnouncedTo).IsEqualTo(0u);
            await Assert.That(hull.ZoneSimPendingFor).IsEqualTo(0u);
        }
        finally
        {
            restore();
        }
    }

    [Test]
    public async Task CollectBoatAttachments_WalksNestedChildrenFirst()
    {
        var hull = new Slave { ObjId = 2339 };
        var mast = new Slave { ObjId = 2348 };
        var lantern = new Slave { ObjId = 2401 };
        mast.AttachedSlaves.Add(lantern);
        hull.AttachedSlaves.Add(mast);
        hull.AttachedDoodads.Add(new Doodad { ObjId = 2365 });

        var slaves = new List<Slave>();
        var doodads = new List<Doodad>();
        SlaveManager.CollectBoatAttachments(hull, slaves, doodads);

        await Assert.That(slaves.Select(s => s.ObjId)).IsEquivalentTo(new uint[] { 2401, 2348 });
        await Assert.That(doodads.Select(d => d.ObjId)).IsEquivalentTo(new uint[] { 2365 });
    }

    [Test]
    public async Task FinalizeBoatDespawn_IsIdempotent()
    {
        var units = new List<uint>();
        HookRelays(units, [], out var restore);
        try
        {
            var hull = new Slave { ObjId = 10, IsDespawning = true, ZoneAnnouncedTo = 186 };

            SlaveManager.FinalizeBoatDespawn(hull);
            SlaveManager.FinalizeBoatDespawn(hull);

            await Assert.That(units.Count(id => id == 10)).IsEqualTo(1);
            await Assert.That(hull.DespawnFinalized).IsTrue();
            await Assert.That(hull.ObjId).IsEqualTo(0u);
        }
        finally
        {
            restore();
        }
    }

    private static Slave HullWithGrowlingKit()
    {
        var hull = new Slave { ObjId = 2339 };
        hull.AttachedSlaves.Add(new Slave { ObjId = 2346 });
        hull.AttachedSlaves.Add(new Slave { ObjId = 2348 });
        hull.AttachedSlaves.Add(new Slave { ObjId = 2349 });
        hull.AttachedDoodads.Add(new Doodad { ObjId = 2365 });
        return hull;
    }

    private static void HookRelays(
        List<uint> units,
        List<uint> doodads,
        out Action restore,
        Action<uint> onZone = null)
    {
        var prevUnit = WorldIntegration.RelayUnitRemovedToZoneId;
        var prevDoodad = WorldIntegration.RelayRemoveDoodadToZoneId;
        WorldIntegration.RelayUnitRemovedToZoneId = (zone, id) =>
        {
            onZone?.Invoke(zone);
            units.Add(id);
        };
        WorldIntegration.RelayRemoveDoodadToZoneId = (_, id) => doodads.Add(id);
        restore = () =>
        {
            WorldIntegration.RelayUnitRemovedToZoneId = prevUnit;
            WorldIntegration.RelayRemoveDoodadToZoneId = prevDoodad;
        };
    }
}
