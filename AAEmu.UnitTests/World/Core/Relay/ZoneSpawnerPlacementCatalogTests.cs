using AAEmu.World.Core.Relay;

namespace AAEmu.UnitTests.World.Core.Relay;

public class ZoneSpawnerPlacementCatalogTests
{
    [Test]
    public async Task ParseFile_ReadsValidSpawnerBlocks()
    {
        var path = Path.Combine(Path.GetTempPath(), $"npc_spawners_{Guid.NewGuid():N}.g");
        await File.WriteAllTextAsync(path, """
            spawner
            {
            	spawnerId 1001
            	spawnerType 9846
            	pos( x 10.5, y 20.25, z 30 )
            	zRot 1.5
            }
            spawner
            {
            	spawnerId 1002
            	spawnerType 9848
            	pos( x -1, y 2, z 3.5 )
            }
            """);
        try
        {
            var list = ZoneSpawnerPlacementCatalog.ParseFile(path);
            await Assert.That(list.Count).IsEqualTo(2);
            await Assert.That(list[0].PlacementId).IsEqualTo(1001u);
            await Assert.That(list[0].SpawnerType).IsEqualTo(9846u);
            await Assert.That(list[0].X).IsEqualTo(10.5f);
            await Assert.That(list[1].PlacementId).IsEqualTo(1002u);
            await Assert.That(list[1].SpawnerType).IsEqualTo(9848u);
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
        await Assert.That(() => ZoneSpawnerPlacementCatalog.ParseFile(missing))
            .Throws<FileNotFoundException>();
    }

    [Test]
    public async Task ParseFile_SkipsInvalidBlocks()
    {
        var path = Path.Combine(Path.GetTempPath(), $"npc_spawners_bad_{Guid.NewGuid():N}.g");
        await File.WriteAllTextAsync(path, """
            spawner
            {
            	spawnerId not-a-number
            	spawnerType 1
            	pos( x 1, y 2, z 3 )
            }
            spawner
            {
            	spawnerId 7
            	spawnerType 8
            	pos( x 1, y 2, z 3 )
            }
            """);
        try
        {
            var list = ZoneSpawnerPlacementCatalog.ParseFile(path);
            await Assert.That(list.Count).IsEqualTo(1);
            await Assert.That(list[0].PlacementId).IsEqualTo(7u);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
