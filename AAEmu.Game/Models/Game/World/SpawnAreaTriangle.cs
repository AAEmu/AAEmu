using System.Numerics;

namespace AAEmu.Game.Models.Game.World;

/// <summary>
/// One weighted triangle of an area spawner's roaming polygon. The triangulation
/// comes straight from the client's npc_spawners.g; <see cref="Rate"/> is the
/// triangle's fraction of the total polygon area and is used to pick a triangle
/// in proportion to its size so spawns are spread uniformly across the polygon.
/// </summary>
public sealed class SpawnAreaTriangle
{
    public Vector3 A { get; init; }
    public Vector3 B { get; init; }
    public Vector3 C { get; init; }
    public float Rate { get; init; }

    /// <summary>Returns a uniformly random point inside the triangle.</summary>
    public Vector3 RandomPoint()
    {
        var r1 = (float)Random.Shared.NextDouble();
        var r2 = (float)Random.Shared.NextDouble();
        var sqrtR1 = MathF.Sqrt(r1);

        var a = 1f - sqrtR1;
        var b = sqrtR1 * (1f - r2);
        var c = sqrtR1 * r2;

        return a * A + b * B + c * C;
    }
}
