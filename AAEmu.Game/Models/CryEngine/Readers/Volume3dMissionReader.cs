using AAEmu.Commons.Exceptions;
using AAEmu.Game.Models.CryEngine.Entities;

namespace AAEmu.Game.Models.CryEngine.Readers;

/// <summary>
/// Reader for v3dmission*.bai files — 3D volumetric navigation grids used in instances.
/// </summary>
public class Volume3dMissionReader(System.IO.Stream rawStream, uint zoneId) : BaiReader(rawStream, zoneId)
{
    public const int BaiVolume3dNavFileVersion = 21;

    public Volume3dNavRegion NavRegion { get; set; }

    public override void CheckVersion(int version)
    {
        if (version > BaiVolume3dNavFileVersion)
        {
            throw new GameException("Wrong 3D volume nav BAI file version - found " + version + ", expected " + BaiVolume3dNavFileVersion);
        }
    }

    protected override void ReadFromFile()
    {
        NavRegion = new Volume3dNavRegion(ZoneId);
        if (Reader == null)
            return;

        // Bounding box (6 floats: minX, minY, minZ, maxX, maxY, maxZ)
        NavRegion.BMin = ReadVector3(doNotAddOffset: true);
        NavRegion.BMax = ReadVector3(doNotAddOffset: true);

        // Apply point offset to bounding box
        NavRegion.BMin += ReaderPointOffset;
        NavRegion.BMax += ReaderPointOffset;

        // Version repeat + reserved fields
        var versionRepeat = Reader.ReadInt32();
        var reserved1 = Reader.ReadInt32();
        var reserved2 = Reader.ReadInt32();

        // Grid parameters
        var nodeCount = Reader.ReadInt32();
        var gridDim = Reader.ReadInt32();
        NavRegion.CellSizeX = Reader.ReadSingle();
        NavRegion.CellSizeY = Reader.ReadSingle();
        NavRegion.CellSizeZ = Reader.ReadSingle();

        // Compute grid dimensions from bounding box and cell sizes
        if (NavRegion.CellSizeX > 0 && NavRegion.CellSizeY > 0 && NavRegion.CellSizeZ > 0)
        {
            var extent = NavRegion.BMax - NavRegion.BMin;
            NavRegion.GridDimX = (int)MathF.Ceiling(extent.X / NavRegion.CellSizeX);
            NavRegion.GridDimY = (int)MathF.Ceiling(extent.Y / NavRegion.CellSizeY);
            NavRegion.GridDimZ = (int)MathF.Ceiling(extent.Z / NavRegion.CellSizeZ);
        }

        // Second grid dim value
        var gridDim2 = Reader.ReadInt32();

        // Read remaining data as voxel grid
        var remaining = (int)(Reader.BaseStream.Length - Reader.BaseStream.Position);
        if (remaining > 0)
            NavRegion.VoxelData = Reader.ReadBytes(remaining);
    }
}
