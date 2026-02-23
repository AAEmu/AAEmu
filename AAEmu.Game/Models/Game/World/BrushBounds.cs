namespace AAEmu.Game.Models.Game.World;

/// <summary>
/// Axis-aligned bounding box of a static brush object (building, rock, etc.)
/// extracted from object.dat in the game_pak.
/// Uses only 24 bytes per brush for minimal memory footprint.
/// </summary>
public struct BrushBounds
{
    public float MinX, MaxX;
    public float MinY, MaxY;
    public float MinZ, MaxZ;
}
