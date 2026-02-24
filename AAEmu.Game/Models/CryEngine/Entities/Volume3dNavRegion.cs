using System.Numerics;

namespace AAEmu.Game.Models.CryEngine.Entities;

/// <summary>
/// 3D volumetric navigation region from v3dmission BAI files.
/// Contains a 3D voxel grid for indoor/multi-floor navigation in instances.
/// </summary>
public class Volume3dNavRegion(uint zoneId)
{
    public uint ZoneId { get; } = zoneId;
    public Vector3 BMin { get; set; }
    public Vector3 BMax { get; set; }
    public int GridDimX { get; set; }
    public int GridDimY { get; set; }
    public int GridDimZ { get; set; }
    public float CellSizeX { get; set; }
    public float CellSizeY { get; set; }
    public float CellSizeZ { get; set; }

    /// <summary>
    /// Raw voxel data — walkability flags per voxel cell.
    /// Index: [x + y * GridDimX + z * GridDimX * GridDimY]
    /// </summary>
    public byte[] VoxelData { get; set; } = [];
}
