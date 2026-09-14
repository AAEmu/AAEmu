using AAEmu.Game.Core.Managers.World;

namespace AAEmu.UnitTests.Game.Core.Managers.World;

public class DoodadSpawnDuplicateIndexTests
{
    [Test]
    public async Task IndexedFirstWinsMatchesLegacyPredicateAtCoordinateEdges()
    {
        (uint UnitId, float X, float Y, float Z)[] points =
        [
            (1, 0f, 0f, 0f),
            (1, 0.009999f, -0.009999f, 0.009999f),
            (1, 0.01f, 0f, 0f),
            (2, 0.009999f, 0f, 0f),
            (1, -0.010001f, -0.010001f, -0.010001f),
            (1, -0.019999f, -0.019999f, -0.019999f),
            (3, float.MaxValue, 1f, 1f),
            (3, float.MaxValue, 1f, 1f),
            (4, float.NaN, 1f, 1f),
            (4, float.NaN, 1f, 1f),
            (5, float.PositiveInfinity, 1f, 1f),
            (5, float.PositiveInfinity, 1f, 1f)
        ];

        await AssertSameOrderedSurvivors(points);
    }

    [Test]
    public async Task IndexedFirstWinsMatchesLegacyPredicateForLargeMixedInput()
    {
        var random = new Random(739_113);
        var points = new List<(uint UnitId, float X, float Y, float Z)>();
        for (var index = 0; index < 5_000; index++)
        {
            var unitId = (uint)random.Next(1, 80);
            var x = (float)(random.NextDouble() * 20_000 - 10_000);
            var y = (float)(random.NextDouble() * 20_000 - 10_000);
            var z = (float)(random.NextDouble() * 1_000 - 500);
            points.Add((unitId, x, y, z));
            if (index % 7 == 0)
                points.Add((unitId, x + 0.004f, y - 0.004f, z + 0.004f));
            if (index % 113 == 0)
                points.Add((unitId + 1, x, y, z));
        }

        await AssertSameOrderedSurvivors(points);
    }

    private static async Task AssertSameOrderedSurvivors(
        IReadOnlyList<(uint UnitId, float X, float Y, float Z)> points)
    {
        var indexed = IndexedSurvivors(points);
        var legacy = LegacySurvivors(points);
        await Assert.That(indexed.Length).IsEqualTo(legacy.Length);
        for (var position = 0; position < legacy.Length; position++)
            await Assert.That(indexed[position]).IsEqualTo(legacy[position]);
    }

    private static int[] IndexedSurvivors(
        IReadOnlyList<(uint UnitId, float X, float Y, float Z)> points)
    {
        var index = new DoodadSpawnDuplicateIndex();
        var survivors = new List<int>();
        for (var position = 0; position < points.Count; position++)
        {
            var point = points[position];
            if (index.Contains(point.UnitId, point.X, point.Y, point.Z))
                continue;
            survivors.Add(position);
            index.Add(point.UnitId, point.X, point.Y, point.Z);
        }
        return survivors.ToArray();
    }

    private static int[] LegacySurvivors(
        IReadOnlyList<(uint UnitId, float X, float Y, float Z)> points)
    {
        var survivors = new List<int>();
        for (var position = 0; position < points.Count; position++)
        {
            var point = points[position];
            if (survivors.Any(index => points[index].UnitId == point.UnitId &&
                                       Math.Abs(points[index].X - point.X) < 0.01f &&
                                       Math.Abs(points[index].Y - point.Y) < 0.01f &&
                                       Math.Abs(points[index].Z - point.Z) < 0.01f))
                continue;
            survivors.Add(position);
        }
        return survivors.ToArray();
    }
}
