using AAEmu.Game.Models.Game.World;

namespace AAEmu.UnitTests.Game.Models.Game.World;

/// <summary>
/// A zone's arrival point (<c>spawn_point.g</c>) and the lookup that finds it for a world/zone.
/// </summary>
/// <remarks>
/// An instance's entry point comes from this file, so a parse that misses the object, reads the wrong
/// number, or a lookup that finds the wrong zone leaves the player at the zone origin instead of the
/// level's designed arrival point.
/// </remarks>
public class ZoneSpawnPointFileRulesTests
{
    [Test]
    public async Task Parse_ReadsPosZRotAndRadius()
    {
        const string body = """
            object
                name LevelDesignAreaSphere_1
                pos ( x 554.128, y 634.454, z 126.451 )
                zRot 0.523599
                radius 3
            """;

        var rows = ZoneSpawnPointFileRules.Parse(body);
        await Assert.That(rows.Count).IsEqualTo(1);
        await Assert.That(rows[0].Name).IsEqualTo("LevelDesignAreaSphere_1");
        await Assert.That(rows[0].X).IsEqualTo(554.128f);
        await Assert.That(rows[0].Y).IsEqualTo(634.454f);
        await Assert.That(rows[0].Z).IsEqualTo(126.451f);
        await Assert.That(rows[0].ZRotRadians).IsEqualTo(0.523599f);
        await Assert.That(rows[0].Radius).IsEqualTo(3f);
    }

    [Test]
    public async Task Parse_KeepsEveryObjectInFileOrder()
    {
        // A battleground names one spawn per side; the parse must not assume a single name.
        const string body = """
            object
                name spawn_
                pos ( x 693.73, y 727.379, z 185.295 )
                zRot 1.23918
                radius 3
            object
                name spawn_red
                pos ( x 1.5, y 2.5, z 3.5 )
            """;

        var rows = ZoneSpawnPointFileRules.Parse(body);
        await Assert.That(rows.Count).IsEqualTo(2);
        await Assert.That(rows[0].Name).IsEqualTo("spawn_");
        await Assert.That(rows[0].X).IsEqualTo(693.73f);
        await Assert.That(rows[1].Name).IsEqualTo("spawn_red");
        await Assert.That(rows[1].ZRotRadians).IsEqualTo(0f);
        await Assert.That(rows[1].Radius).IsEqualTo(0f);
    }

    [Test]
    public async Task Parse_SkipsBlocksWithoutAPosition()
    {
        const string body = "object\n    name only_a_name\n";
        await Assert.That(ZoneSpawnPointFileRules.Parse(body).Count).IsEqualTo(0);
        await Assert.That(ZoneSpawnPointFileRules.Parse("").Count).IsEqualTo(0);
        await Assert.That(ZoneSpawnPointFileRules.Parse(null).Count).IsEqualTo(0);
    }

    [Test]
    public async Task TryParseFirst_TakesTheFirstPositionedObject()
    {
        const string body = """
            object
                name first
                pos ( x 10, y 20, z 30 )
            object
                name second
                pos ( x 40, y 50, z 60 )
            """;

        await Assert.That(ZoneSpawnPointFileRules.TryParseFirst(body, out var spawn)).IsTrue();
        await Assert.That(spawn.Name).IsEqualTo("first");
        await Assert.That(spawn.X).IsEqualTo(10f);
        await Assert.That(ZoneSpawnPointFileRules.TryParseFirst("object\n  name x\n", out _)).IsFalse();
    }

    [Test]
    public async Task YawDegrees_TurnsAuthorRadiansIntoTheSpawnTablesDegrees()
    {
        // The editor stores zRot in radians; the game's spawn tables carry yaw in degrees.
        await Assert.That(MathF.Abs(ZoneSpawnPointFileRules.YawDegreesFromZRot(0.523599f) - 30f)).IsLessThan(0.01f);
        await Assert.That(MathF.Abs(ZoneSpawnPointFileRules.YawDegreesFromZRot(MathF.PI) - 180f)).IsLessThan(0.01f);
    }

    [Test]
    public async Task ToWorldCoordinates_ShiftsZoneLocalByTheOriginCell()
    {
        // instance_eternity's zone occupies cells from (1,1); the file's (554,634) is inside that cell, so
        // the continent point is one cell over. Applying the raw point landed the player in cell (0,0) —
        // empty level, under the map.
        var world = ZoneSpawnPointFileRules.ToWorldCoordinates(1f, 1f, 554.128f, 634.454f, 126.451f);
        await Assert.That(MathF.Abs(world.X - 1578.128f)).IsLessThan(0.001f);
        await Assert.That(MathF.Abs(world.Y - 1658.454f)).IsLessThan(0.001f);
        await Assert.That(world.Z).IsEqualTo(126.451f);

        // A zone whose origin cell is (0,0) keeps its coordinates unchanged.
        var flat = ZoneSpawnPointFileRules.ToWorldCoordinates(0f, 0f, 693.73f, 727.379f, 185.295f);
        await Assert.That(flat.X).IsEqualTo(693.73f);
        await Assert.That(flat.Y).IsEqualTo(727.379f);
    }

    [Test]
    public async Task Catalog_FindsTheZonesFileAndIgnoresOtherZones()
    {
        var root = Path.Combine(Path.GetTempPath(), "aaemu-spawn-" + Guid.NewGuid().ToString("N"));
        var dir = Path.Combine(root, "worlds", "instance_eternity", "level_design", "zone", "373", "world_server");
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(
            Path.Combine(dir, "spawn_point.g"),
            "object\n    name LevelDesignAreaSphere_1\n    pos ( x 554.128, y 634.454, z 126.451 )\n    zRot 0.523599\n    radius 3\n");
        try
        {
            await Assert.That(ZoneSpawnPointGCatalog.TryGetZoneSpawn([root], "instance_eternity", 373u, out var spawn))
                .IsTrue();
            await Assert.That(spawn.X).IsEqualTo(554.128f);
            await Assert.That(spawn.ZRotRadians).IsEqualTo(0.523599f);

            // A different zone of the same world, and a world with no file, must not resolve.
            await Assert.That(ZoneSpawnPointGCatalog.TryGetZoneSpawn([root], "instance_eternity", 374u, out _)).IsFalse();
            await Assert.That(ZoneSpawnPointGCatalog.TryGetZoneSpawn([root], "instance_hadir_farm", 241u, out _)).IsFalse();
            await Assert.That(ZoneSpawnPointGCatalog.TryGetZoneSpawn([root], "instance_eternity", 0u, out _)).IsFalse();
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
