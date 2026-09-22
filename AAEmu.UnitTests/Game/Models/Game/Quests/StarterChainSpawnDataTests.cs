using AAEmu.Game.Models.Game.Quests;

using Newtonsoft.Json;

namespace AAEmu.UnitTests.Game.Models.Game.Quests;

/// <summary>
/// Guards the shipped <c>Data/Worlds/main_world/doodad_spawns.json</c> (copied next to the test
/// binary by the Game project) against losing a starter chain doodad. Chain NPCs are not in the
/// repo: the zone host reads the client's <c>npc_spawners.g</c> itself, so only the doodads can be
/// pinned here. The two <c>npctype://</c> report bodies were absent until this data change; their
/// coordinates come from the cell <c>doodad.g</c> rows named in the entries' titles.
/// </summary>
public class StarterChainSpawnDataTests
{
    private sealed class SpawnRow
    {
        public uint UnitId { get; set; }
        public PositionRow Position { get; set; }
    }

    private sealed class PositionRow
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float Yaw { get; set; }
    }

    private static Dictionary<uint, List<PositionRow>> ReadSpawns(IEnumerable<uint> wanted)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Data", "Worlds", "main_world", "doodad_spawns.json");
        var found = wanted.Distinct().ToDictionary(id => id, _ => new List<PositionRow>());

        using var text = File.OpenText(path);
        using var reader = new JsonTextReader(text);
        var serializer = new JsonSerializer();
        if (!reader.Read() || reader.TokenType != JsonToken.StartArray)
            throw new InvalidDataException($"{path} does not start with an array");

        while (reader.Read() && reader.TokenType == JsonToken.StartObject)
        {
            var row = serializer.Deserialize<SpawnRow>(reader);
            if (row?.Position != null && found.TryGetValue(row.UnitId, out var list))
                list.Add(row.Position);
        }

        return found;
    }

    private static PositionRow Single(Dictionary<uint, List<PositionRow>> spawns, uint templateId)
    {
        var rows = spawns[templateId];
        if (rows.Count != 1)
            throw new InvalidDataException($"doodad {templateId} has {rows.Count} spawns, expected one");
        return rows[0];
    }

    [Test]
    public async Task ShippedSpawns_ContainEveryStarterChainDoodad()
    {
        var required = StarterChainActorRules.Collect(StarterChainActorRulesTests.AllChains);
        var spawns = ReadSpawns(required.Doodads);

        await Assert.That(required.Doodads.Count).IsEqualTo(7);
        foreach (var templateId in required.Doodads)
            await Assert.That(spawns[templateId].Count > 0).IsTrue();
    }

    [Test]
    public async Task ShippedSpawns_PlaceElfArenaBodyOnItsLevelPackRow()
    {
        // level_design/cells/010_014/doodad.g: type 14178, pos (27.3228, 931.893, 239.544),
        // ori (0, 0, 0.93358, -0.358368); cell 10,14 puts it at 10240 + x, 14336 + y.
        var row = Single(ReadSpawns([14178u]), 14178);

        await Assert.That(row.X).IsEqualTo(10267.3228f).Within(0.01f);
        await Assert.That(row.Y).IsEqualTo(15267.893f).Within(0.01f);
        await Assert.That(row.Z).IsEqualTo(239.544f).Within(0.01f);
        await Assert.That(row.Yaw).IsEqualTo(-138f).Within(0.01f);
    }

    [Test]
    public async Task ShippedSpawns_PlaceDwarfDaughterBodyOnItsLevelPackRow()
    {
        // level_design/cells/008_013/doodad.g: type 14206, pos (265.12, 155.49, 696.871),
        // ori (0, 0, 0.996918, 0.0784556); cell 8,13 puts it at 8192 + x, 13312 + y.
        var row = Single(ReadSpawns([14206u]), 14206);

        await Assert.That(row.X).IsEqualTo(8457.12f).Within(0.01f);
        await Assert.That(row.Y).IsEqualTo(13467.49f).Within(0.01f);
        await Assert.That(row.Z).IsEqualTo(696.871f).Within(0.01f);
        await Assert.That(row.Yaw).IsEqualTo(171f).Within(0.01f);
    }
}
