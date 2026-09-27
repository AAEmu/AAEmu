namespace AAEmu.Game.Models.Game.World;

/// <summary>
/// Walkable AI building volume from <c>areasmission</c> NavigationModifiers
/// (<see cref="BuildingId"/> + vertical band). Used only for Floor seating — not pathfinding.
/// </summary>
public readonly struct BuildingVolume
{
    public int BuildingId { get; init; }
    public float MinZ { get; init; }
    public float MaxZ { get; init; }
    public float Height { get; init; }

    /// <summary>True when <paramref name="z"/> lies in the volume band (optional slack).</summary>
    public bool ContainsZ(float z, float slack = 0f)
    {
        return z >= MinZ - slack && z <= MaxZ + slack;
    }
}
