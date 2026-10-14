using AAEmu.Game.Models.Game.Quests;
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
    public async Task ParseFile_011_011Pad_ConvertsEhnoirAndFeosToWorld()
    {
        var path = Path.Combine(Path.GetTempPath(), $"doodad_3901_{Guid.NewGuid():N}.g");
        await File.WriteAllTextAsync(path, """
            doodad
                category 17
                type 14226
                family 0
                vegetation false
                pos ( x 282.626, y 565.929, z 110.011 )
                ori ( x 0, y 0, z 0.951057, w 0.309017 )
                scale 1
            doodad
                category 17
                type 14227
                family 0
                vegetation false
                pos ( x 280.631, y 563.183, z 110.172 )
                ori ( x 0, y 0, z -0.325568, w 0.945519 )
                scale 1
            doodad
                category 17
                type 14220
                family 0
                vegetation false
                pos ( x 204.733, y 286.127, z 124.619 )
                ori ( x 0, y 0, z 0.0174524, w 0.999848 )
                scale 1
            """);
        try
        {
            var list = ZoneDoodadPlacementCatalog.ParseFile(path, cellX: 11, cellY: 11);
            await Assert.That(list.Count).IsEqualTo(3);
            await Assert.That(list[0].TemplateId).IsEqualTo(14226u);
            await Assert.That(list[0].X).IsEqualTo(11546.626f).Within(0.001f);
            await Assert.That(list[0].Y).IsEqualTo(11829.929f).Within(0.001f);
            await Assert.That(list[1].TemplateId).IsEqualTo(14227u);
            await Assert.That(list[1].X).IsEqualTo(11544.631f).Within(0.001f);
            await Assert.That(list[1].Y).IsEqualTo(11827.183f).Within(0.001f);
            await Assert.That(list[2].TemplateId).IsEqualTo(14220u);
            await Assert.That(list[2].X).IsEqualTo(11468.733f).Within(0.001f);
            await Assert.That(list[2].Y).IsEqualTo(11550.127f).Within(0.001f);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task GetByTemplates_ThenPlanCompanions_TakesPadSkipsPlaza()
    {
        const string world = "companion_plan_test_world";
        ZoneDoodadPlacementCatalog.SeedIndexForTests(world,
        [
            new(14226, 11546.626f, 11829.929f, 110.011f, 0f),
            new(14227, 11544.631f, 11827.183f, 110.172f, 0f),
            new(14228, 11538.821f, 11828.881f, 110.356f, 0f),
            new(14178, 11468.733f, 11550.127f, 124.619f, 0f)
        ]);
        try
        {
            var talk = ToRules(ZoneDoodadPlacementCatalog.GetByTemplates(world, [14226u]));
            var npcType = ToRules(
                ZoneDoodadPlacementCatalog.GetByTemplates(world, [14226u, 14227u, 14228u, 14178u]));
            var planned = QuestTalkDoodadRules.PlanCompanions([14226u], talk, npcType, []);

            await Assert.That(planned.Any(p => p.TemplateId == 14227)).IsTrue();
            await Assert.That(planned.Count(p => p.TemplateId == 14228)).IsEqualTo(1);
            await Assert.That(planned.Any(p => p.TemplateId is 14226 or 14178)).IsFalse();
        }
        finally
        {
            ZoneDoodadPlacementCatalog.Invalidate(world);
        }
    }

    private static List<QuestTalkDoodadRules.Placement> ToRules(
        IReadOnlyList<ZoneDoodadPlacementCatalog.DoodadPlacement> catalog)
    {
        var list = new List<QuestTalkDoodadRules.Placement>(catalog.Count);
        foreach (var p in catalog)
            list.Add(new QuestTalkDoodadRules.Placement(p.TemplateId, p.X, p.Y, p.Z, p.YawDegrees));
        return list;
    }

    [Test]
    public async Task ParseIgnoreDoodadTypes_ReadsOpenAndIgnoreLists()
    {
        var ids = ZoneDoodadPlacementCatalog.ParseIgnoreDoodadTypes("""
            ignore_spawners
                doodadType 8411
                doodadType 8412
            """);
        await Assert.That(ids.SetEquals([8411u, 8412u])).IsTrue();
        await Assert.That(ZoneDoodadPlacementCatalog.IsIgnoreListFileName("doodad_open_03.g")).IsTrue();
        await Assert.That(ZoneDoodadPlacementCatalog.IsIgnoreListFileName("ignore_doodad_spawners_01.g")).IsTrue();
        await Assert.That(ZoneDoodadPlacementCatalog.IsIgnoreListFileName("doodad.g")).IsFalse();
    }

    [Test]
    public async Task GetByTemplates_MarksIgnoredPermanentFromSeed()
    {
        const string world = "ignore_flag_test_world";
        ZoneDoodadPlacementCatalog.SeedIndexForTests(world,
        [
            new(14226, 11546.626f, 11829.929f, 110.011f, 0f),
            new(8411, 20113.5f, 21012.5f, 102.8f, 0f, IgnoredPermanent: true)
        ]);
        try
        {
            var list = ZoneDoodadPlacementCatalog.GetByTemplates(world, [14226u, 8411u]);
            await Assert.That(list.Single(p => p.TemplateId == 14226).IgnoredPermanent).IsFalse();
            await Assert.That(list.Single(p => p.TemplateId == 8411).IgnoredPermanent).IsTrue();
        }
        finally
        {
            ZoneDoodadPlacementCatalog.Invalidate(world);
        }
    }

    [Test]
    public async Task GetAll_ThenPlan_PlantsEveryCellRowNotAlreadyStanding()
    {
        const string world = "copy_plant_test_world";
        ZoneDoodadPlacementCatalog.SeedIndexForTests(world,
        [
            new(13976, 1537.606f, 1657.114f, 124.023f, 87f),
            new(14252, 1530.0f, 1660.0f, 123.6f, 0f),
            new(14252, 1532.0f, 1662.0f, 123.6f, 0f),
            new(14724, 1331.1f, 1648.3f, 103.2f, 0f)
        ]);
        try
        {
            var all = ZoneDoodadPlacementCatalog.GetAll(world);
            await Assert.That(all.Count).IsEqualTo(4);

            var places = ToRules(all);
            var wanted = places.Select(p => p.TemplateId).ToHashSet();
            var standing = new List<QuestTalkDoodadRules.Existing> { new(14252, 1530.2f, 1660.1f, 123.6f) };
            var planned = QuestTalkDoodadRules.Plan(wanted, places, standing);

            await Assert.That(planned.Count).IsEqualTo(3);
            await Assert.That(planned.Count(p => p.TemplateId == 14252)).IsEqualTo(1);
            await Assert.That(planned.Single(p => p.TemplateId == 14252).X).IsEqualTo(1532.0f);
        }
        finally
        {
            ZoneDoodadPlacementCatalog.Invalidate(world);
        }
    }

    [Test]
    public async Task GetAll_UnknownWorldIsEmpty()
    {
        await Assert.That(ZoneDoodadPlacementCatalog.GetAll("")).IsEmpty();
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
