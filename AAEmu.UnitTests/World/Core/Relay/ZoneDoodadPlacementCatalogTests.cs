using AAEmu.World.Core.Relay;

namespace AAEmu.UnitTests.World.Core.Relay;

public class ZoneDoodadPlacementCatalogTests
{
    [Test]
    public async Task TryParseCellFolderName_ParsesPaddedIndices()
    {
        await Assert.That(ZoneDoodadPlacementCatalog.TryParseCellFolderName("019_020", out var x, out var y))
            .IsTrue();
        await Assert.That(x).IsEqualTo(19);
        await Assert.That(y).IsEqualTo(20);
        await Assert.That(ZoneDoodadPlacementCatalog.TryParseCellFolderName("bad", out _, out _)).IsFalse();
    }

    [Test]
    public async Task YawDegreesFromOri_MatchesPureZRotation()
    {
        await Assert.That(ZoneDoodadPlacementCatalog.YawDegreesFromOri(0, 0, 0, 1)).IsEqualTo(0f);
        await Assert.That(ZoneDoodadPlacementCatalog.YawDegreesFromOri(0, 0, -0.707107f, 0.707107f))
            .IsEqualTo(-90f).Within(0.01f);
        await Assert.That(ZoneDoodadPlacementCatalog.YawDegreesFromOri(0, 0, 1f, 4.37114e-008f))
            .IsEqualTo(180f).Within(0.01f);
    }

    [Test]
    public async Task ParseFile_ConvertsCellLocalToWorld()
    {
        var path = Path.Combine(Path.GetTempPath(), $"doodad_{Guid.NewGuid():N}.g");
        await File.WriteAllTextAsync(path, """
            doodad
                category 17
                type 8410
                family 41007
                vegetation false
                pos ( x 657.513, y 532.532, z 102.865 )
                ori ( x 0, y 0, z 0, w 1 )
                scale 1
            doodad
                category 17
                type 8414
                family 0
                vegetation false
                pos ( x 643.055, y 523.568, z 101.424 )
                ori ( x 0, y 0, z -0.707107, w 0.707107 )
                scale 1
            """);
        try
        {
            var list = ZoneDoodadPlacementCatalog.ParseFile(path, cellX: 19, cellY: 20);
            await Assert.That(list.Count).IsEqualTo(2);
            await Assert.That(list[0].TemplateId).IsEqualTo(8410u);
            await Assert.That(list[0].X).IsEqualTo(20113.513f).Within(0.001f);
            await Assert.That(list[0].Y).IsEqualTo(21012.532f).Within(0.001f);
            await Assert.That(list[0].Z).IsEqualTo(102.865f).Within(0.001f);
            await Assert.That(list[0].YawDegrees).IsEqualTo(0f).Within(0.01f);
            await Assert.That(list[1].TemplateId).IsEqualTo(8414u);
            await Assert.That(list[1].X).IsEqualTo(20099.055f).Within(0.001f);
            await Assert.That(list[1].YawDegrees).IsEqualTo(-90f).Within(0.01f);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task ParseFile_MissingPath_Throws()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"missing_{Guid.NewGuid():N}.g");
        await Assert.That(() => ZoneDoodadPlacementCatalog.ParseFile(missing, 0, 0))
            .Throws<FileNotFoundException>();
    }

    [Test]
    public async Task ParseFile_SkipsBlocksWithoutTypeOrPos()
    {
        var path = Path.Combine(Path.GetTempPath(), $"doodad_bad_{Guid.NewGuid():N}.g");
        await File.WriteAllTextAsync(path, """
            doodad
                category 17
                family 0
                pos ( x 1, y 2, z 3 )
            doodad
                category 17
                type 7
                vegetation false
                pos ( x 10, y 20, z 30 )
                ori ( x 0, y 0, z 0, w 1 )
            """);
        try
        {
            var list = ZoneDoodadPlacementCatalog.ParseFile(path, 0, 0);
            await Assert.That(list.Count).IsEqualTo(1);
            await Assert.That(list[0].TemplateId).IsEqualTo(7u);
            await Assert.That(list[0].X).IsEqualTo(10f);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
