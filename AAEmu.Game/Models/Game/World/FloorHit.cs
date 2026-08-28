namespace AAEmu.Game.Models.Game.World;

/// <summary>
/// Result of a floor query, including comparison samples for debug.
/// </summary>
public readonly struct FloorHit
{
    public float Z { get; init; }
    public FloorSource Source { get; init; }
    public float TerrainZ { get; init; }
    public float NavNodeZ { get; init; }

    public float DeltaNav => MathF.Abs(Z - NavNodeZ);
    public float DeltaTerrain => MathF.Abs(Z - TerrainZ);
}
