using System.Numerics;
using System.Text;
using AAEmu.Game.IO;
using NLog;

namespace AAEmu.Game.Models.ClientData;

/// <summary>
/// Reads cover point data from cover.ctc files (CryEngine terrain cover format).
/// The CTC format is a quadtree-based surface type / cover surface map.
/// This reader extracts surface cover metadata from the file header and grid.
/// </summary>
public class CoverCtcFile(string fileName)
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public string FileName { get; } = fileName;
    public List<CoverPoint> CoverPoints { get; } = [];
    public int Version { get; private set; }
    public int GridSizeX { get; private set; }
    public int GridSizeY { get; private set; }

    public bool ReadFile()
    {
        using var sourceFs = ClientFileManager.GetFileStream(FileName);
        if (sourceFs == null)
            return false;

        using var ms = new MemoryStream();
        sourceFs.CopyTo(ms);
        ms.Position = 0;

        using var br = new BinaryReader(ms);

        // Read CRY header magic
        var magic = Encoding.ASCII.GetString(br.ReadBytes(4));
        if (!magic.StartsWith("CRY"))
        {
            Logger.Warn($"cover.ctc {FileName}: invalid magic '{magic}', expected 'CRY'");
            return false;
        }

        // Read header fields
        Version = br.ReadInt32();    // e.g., 100
        var flags = br.ReadInt32();  // e.g., 8
        var numNodes = br.ReadInt32();
        var cellSize = br.ReadSingle();
        GridSizeX = br.ReadInt32();
        var numSurfaceTypes = br.ReadInt32();

        GridSizeY = GridSizeX; // Assume square grid

        // The rest of the file is a quadtree with surface type indices.
        // The exact layout of cover points in CTC is complex (CryEngine octree format).
        // For now we read and store the header metadata. Cover point extraction from
        // the quadtree will be implemented when the binary format is fully reverse-engineered.

        Logger.Trace($"cover.ctc {FileName}: v{Version}, {GridSizeX}x{GridSizeY}, " +
                     $"{numSurfaceTypes} surface types, {numNodes} nodes, {ms.Length} bytes");

        return true;
    }
}
