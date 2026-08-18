using System.Reflection;

using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Slaves;
using AAEmu.Game.Models.Game.StreamAoi;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.World.Xml;

namespace AAEmu.UnitTests.Game.Models.Game.StreamAoi;

public class SlaveStreamAoiTests
{
    [Test]
    public async Task CanStreamSlaveNow_ShipUsesEnterBand()
    {
        StreamAoiTable.ReplaceConfig(new StreamAoiConfig());
        var (character, hull, world) = CreateShipAt(224f);
        character.ArmMirrorNpcStream();

        await Assert.That(character.CanStreamSlaveNow(hull)).IsTrue();

        hull.Transform.Local.SetPosition(226f, 0f, 0f);
        await Assert.That(character.CanStreamSlaveNow(hull)).IsFalse();
        world.Dispose();
    }

    [Test]
    public async Task CullStreamedSlavesBeyondAoi_UnselectsHullPastShipExit()
    {
        StreamAoiTable.ReplaceConfig(new StreamAoiConfig());
        var (character, hull, world) = CreateShipAt(247f);
        character.ArmMirrorNpcStream();
        character.MarkSlaveStreamed(hull);

        await Assert.That(character.CullStreamedSlavesBeyondAoi()).IsEqualTo(0);
        await Assert.That(character.StreamedSlaveIds.ContainsKey(hull.ObjId)).IsTrue();

        hull.Transform.Local.SetPosition(249f, 0f, 0f);
        await Assert.That(character.CullStreamedSlavesBeyondAoi()).IsEqualTo(1);
        await Assert.That(character.StreamedSlaveIds.ContainsKey(hull.ObjId)).IsFalse();
        // No region neighbours in this fixture — do not re-queue.
        await Assert.That(character.HasPendingSlaves).IsFalse();

        world.Dispose();
    }

    [Test]
    public async Task CullStreamedSlavesBeyondAoi_LeavesEquipmentParts()
    {
        StreamAoiTable.ReplaceConfig(new StreamAoiConfig());
        var (character, part, world) = CreateSlaveAt(10_000f, SlaveKind.SlaveEquipment);
        character.ArmMirrorNpcStream();
        character.MarkSlaveStreamed(part);

        await Assert.That(character.CullStreamedSlavesBeyondAoi()).IsEqualTo(0);
        await Assert.That(character.StreamedSlaveIds.ContainsKey(part.ObjId)).IsTrue();

        world.Dispose();
    }

    [Test]
    public async Task CullStreamedSlavesBeyondAoi_HaulerUsesAmbientExit()
    {
        StreamAoiTable.ReplaceConfig(new StreamAoiConfig());
        var (character, hauler, world) = CreateSlaveAt(109f, SlaveKind.Machine);
        character.ArmMirrorNpcStream();
        character.MarkSlaveStreamed(hauler);

        await Assert.That(character.CullStreamedSlavesBeyondAoi()).IsEqualTo(0);

        hauler.Transform.Local.SetPosition(111f, 0f, 0f);
        await Assert.That(character.CullStreamedSlavesBeyondAoi()).IsEqualTo(1);
        await Assert.That(character.StreamedSlaveIds.ContainsKey(hauler.ObjId)).IsFalse();

        world.Dispose();
    }

    [Test]
    public async Task TryFlushPendingSlaves_PaintsHullInsideEnter()
    {
        StreamAoiTable.ReplaceConfig(new StreamAoiConfig());
        var (character, hull, world) = CreateShipAt(200f);
        hull.IsVisible = true;
        character.ArmMirrorNpcStream();
        character.EnqueuePendingSlave(hull);

        await Assert.That(character.TryFlushPendingSlaves()).IsEqualTo(1);
        await Assert.That(character.StreamedSlaveIds.ContainsKey(hull.ObjId)).IsTrue();
        await Assert.That(character.HasPendingSlaves).IsFalse();

        world.Dispose();
    }

    [Test]
    public async Task TryFlushPendingSlaves_SkipsHullOutsideEnter()
    {
        StreamAoiTable.ReplaceConfig(new StreamAoiConfig());
        var (character, hull, world) = CreateShipAt(300f);
        hull.IsVisible = true;
        character.ArmMirrorNpcStream();
        character.EnqueuePendingSlave(hull);

        await Assert.That(character.TryFlushPendingSlaves()).IsEqualTo(0);
        await Assert.That(character.StreamedSlaveIds.ContainsKey(hull.ObjId)).IsFalse();
        await Assert.That(character.HasPendingSlaves).IsTrue();

        world.Dispose();
    }

    [Test]
    public async Task TryKeepSlaveAcrossRegionLeave_KeepsStreamedHullInsideExit()
    {
        StreamAoiTable.ReplaceConfig(new StreamAoiConfig());
        var (character, hull, world) = CreateShipAt(200f);
        character.ArmMirrorNpcStream();
        character.MarkSlaveStreamed(hull);

        await Assert.That(character.TryKeepSlaveAcrossRegionLeave(hull)).IsTrue();

        hull.Transform.Local.SetPosition(249f, 0f, 0f);
        await Assert.That(character.TryKeepSlaveAcrossRegionLeave(hull)).IsFalse();

        character.ReleaseSlaveSlot(hull.ObjId);
        hull.Transform.Local.SetPosition(200f, 0f, 0f);
        await Assert.That(character.CanStreamSlaveNow(hull)).IsTrue();

        world.Dispose();
    }

    [Test]
    public async Task TryKeepSlaveAcrossRegionLeave_DropsEquipmentParts()
    {
        StreamAoiTable.ReplaceConfig(new StreamAoiConfig());
        var (character, part, world) = CreateSlaveAt(50f, SlaveKind.SlaveEquipment);
        character.ArmMirrorNpcStream();
        character.MarkSlaveStreamed(part);

        await Assert.That(character.TryKeepSlaveAcrossRegionLeave(part)).IsFalse();

        world.Dispose();
    }

    private static (Character character, Slave slave, WorldInstance world) CreateShipAt(float x) =>
        CreateSlaveAt(x, SlaveKind.MerchantShip);

    private static (Character character, Slave slave, WorldInstance world) CreateSlaveAt(float x, SlaveKind kind)
    {
        var world = new WorldInstance(CreateTemplate(), 0, true, 1);
        var character = new Character(new UnitCustomModelParams()) { ObjId = 1 };
        AttachWorld(character, world);
        character.Transform.Local.SetPosition(0f, 0f, 0f);

        var slave = new Slave
        {
            ObjId = 1001,
            Template = new SlaveTemplate { SlaveKind = kind }
        };
        AttachWorld(slave, world);
        slave.Transform.Local.SetPosition(x, 0f, 0f);
        world.AddObject(slave);
        return (character, slave, world);
    }

    // ParentWorld's setter writes Transform.InstanceId, which asks WorldManager.Instance.
    private static void AttachWorld(GameObject obj, WorldInstance world)
    {
        typeof(GameObject)
            .GetField("_parentWorld", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(obj, world);
    }

    private static WorldTemplate CreateTemplate() => new()
    {
        CellX = 1,
        CellY = 1,
        Cells = new WorldCell[0, 0],
        HousingZones = [],
        Id = 0,
        Name = "test_world",
        OceanLevel = 100f,
        SubZones = [],
        XmlWorld = new XmlWorld { Zones = [] },
        XmlWorldZones = [],
        ZoneKeyByRegions = new uint[1, 1],
        ZoneKeys = [0]
    };
}
