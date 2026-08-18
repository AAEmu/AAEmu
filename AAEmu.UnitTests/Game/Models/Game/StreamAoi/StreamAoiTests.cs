using AAEmu.Game.Models.Game.Slaves;
using AAEmu.Game.Models.Game.StreamAoi;

namespace AAEmu.UnitTests.Game.Models.Game.StreamAoi;

public class StreamAoiTests
{
    [Test]
    public async Task SeaBand_Appears225_Gone248()
    {
        StreamAoiTable.ReplaceConfig(new StreamAoiConfig());
        var large = StreamAoiTable.Band(StreamAoiCategory.Large);
        await Assert.That(large.EnterMetres).IsEqualTo(225f);
        await Assert.That(large.ExitMetres).IsEqualTo(248f);

        await Assert.That(StreamAoiTable.IsInside(StreamAoiCategory.Large, 224f * 224f, false)).IsTrue();
        await Assert.That(StreamAoiTable.IsInside(StreamAoiCategory.Large, 226f * 226f, false)).IsFalse();
        await Assert.That(StreamAoiTable.IsInside(StreamAoiCategory.Large, 247f * 247f, true)).IsTrue();
        await Assert.That(StreamAoiTable.IsInside(StreamAoiCategory.Large, 249f * 249f, true)).IsFalse();
    }

    [Test]
    public async Task Ambient_MatchesPlayerAndFarmHauler()
    {
        StreamAoiTable.ReplaceConfig(new StreamAoiConfig());
        var a = StreamAoiTable.Band(StreamAoiCategory.Ambient);
        await Assert.That(a.EnterMetres).IsEqualTo(105f);
        await Assert.That(a.ExitMetres).IsEqualTo(110f);
        await Assert.That(StreamAoiTable.IsInside(StreamAoiCategory.Ambient, 104f * 104f, false)).IsTrue();
        await Assert.That(StreamAoiTable.IsInside(StreamAoiCategory.Ambient, 106f * 106f, false)).IsFalse();
        await Assert.That(StreamAoiTable.IsInside(StreamAoiCategory.Ambient, 109f * 109f, true)).IsTrue();
        await Assert.That(StreamAoiTable.IsInside(StreamAoiCategory.Ambient, 111f * 111f, true)).IsFalse();
    }

    [Test]
    public async Task LargeIds_KrakenAndLeviathanModels()
    {
        StreamAoiTable.ReplaceConfig(new StreamAoiConfig());
        await Assert.That(StreamAoiTable.IsLargeNpc(7607, 0)).IsTrue();
        await Assert.That(StreamAoiTable.IsLargeNpc(14915, 0)).IsTrue();
        await Assert.That(StreamAoiTable.IsLargeNpc(0, 897)).IsTrue();
        await Assert.That(StreamAoiTable.IsLargeNpc(0, 530)).IsTrue();
        await Assert.That(StreamAoiTable.IsLargeNpc(1, 10)).IsFalse();
    }

    [Test]
    public async Task ShipAndBosses_ShareSeaBand()
    {
        StreamAoiTable.ReplaceConfig(new StreamAoiConfig());
        var ship = StreamAoiTable.Band(StreamAoiCategory.Ship);
        var large = StreamAoiTable.Band(StreamAoiCategory.Large);
        await Assert.That(ship.EnterMetres).IsEqualTo(large.EnterMetres);
        await Assert.That(ship.ExitMetres).IsEqualTo(large.ExitMetres);
        await Assert.That(ship.EnterMetres).IsEqualTo(225f);
        await Assert.That(ship.ExitMetres).IsEqualTo(248f);
    }

    [Test]
    public async Task EventHellgate_700m()
    {
        StreamAoiTable.ReplaceConfig(new StreamAoiConfig());
        var e = StreamAoiTable.Band(StreamAoiCategory.Event);
        await Assert.That(e.EnterMetres).IsEqualTo(700f);
        await Assert.That(e.ExitMetres).IsEqualTo(700f);
        await Assert.That(StreamAoiTable.IsInside(StreamAoiCategory.Event, 699f * 699f, false)).IsTrue();
        await Assert.That(StreamAoiTable.IsInside(StreamAoiCategory.Event, 701f * 701f, false)).IsFalse();
    }

    [Test]
    public async Task Part_NeverSoftCulls()
    {
        StreamAoiTable.ReplaceConfig(new StreamAoiConfig());
        await Assert.That(StreamAoiTable.IsInside(StreamAoiCategory.Part, 10_000f * 10_000f, false)).IsTrue();
        await Assert.That(StreamAoiTable.IsInside(StreamAoiCategory.Part, 10_000f * 10_000f, true)).IsTrue();
    }

    [Test]
    public async Task SlaveKinds_HullVsEquipmentVsHauler()
    {
        var clipper = new SlaveTemplate { SlaveKind = SlaveKind.MerchantShip };
        var sail = new SlaveTemplate { SlaveKind = SlaveKind.SlaveEquipment };
        var hauler = new SlaveTemplate { SlaveKind = SlaveKind.Machine };
        await Assert.That(clipper.StreamAoiCategory).IsEqualTo(StreamAoiCategory.Ship);
        await Assert.That(sail.StreamAoiCategory).IsEqualTo(StreamAoiCategory.Part);
        await Assert.That(hauler.StreamAoiCategory).IsEqualTo(StreamAoiCategory.Ambient);
    }
}
