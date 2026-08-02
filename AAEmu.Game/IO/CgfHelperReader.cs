using System.Numerics;
using System.Text;

using NLog;

namespace AAEmu.Game.IO;

/// <summary>
/// Reads the helper nodes out of a CryEngine .cgf. Attach points are stored in the mesh as nodes whose
/// name is the '$'-prefixed string that <c>model_attach_point_strings.prefab</c> names for an attach point
/// id — <c>$driver</c>, <c>$name_plate01</c>, <c>$cannon0</c> and so on.
///
/// Format, read from the shipped meshes rather than a spec:
///   header  'CryTek\0\0' | u32 fileType | u32 version | u32 chunkTableOffset
///   table   u32 count, then count × { u32 chunkType, u32 chunkVersion, u32 offsetInFile, u32 chunkId }
///   node    char name[64] | i32 objectId, parentId, childCount, materialId | 2 bools + 2 pad
///           | float tm[4][4] — translation is the LAST ROW, elements 12..14 | Vec3 pos | …
///
/// Positions are centimetres in the mesh and metres on the server, hence the /100.
/// </summary>
public static class CgfHelperReader
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private const int ChunkTypeNodeLow16 = 0x000B;
    private const int NameLength = 64;
    private const int ChunkHeaderLength = 16;
    private const float CentimetresPerMetre = 100f;

    /// <summary>Maximum plausible chunk count; guards against reading a file that is not a cgf.</summary>
    private const int MaxChunks = 65536;

    /// <summary>
    /// Reads every '$' helper node from a cgf, keyed by node name, with positions converted to metres.
    /// LOD nodes ("$lod1 &lt;mesh&gt;") are excluded — they are parked far off-origin and are not attach points.
    /// Returns an empty map for anything it cannot parse; a mesh without helpers is normal.
    /// </summary>
    public static Dictionary<string, Vector3> ReadHelpers(byte[] data, string sourceName = null)
    {
        var result = new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase);
        if (data == null || data.Length < 20)
            return result;

        if (Encoding.ASCII.GetString(data, 0, 6) != "CryTek")
        {
            Logger.Trace($"CgfHelperReader: {sourceName} is not a cgf");
            return result;
        }

        try
        {
            var tableOffset = BitConverter.ToInt32(data, 16);
            if (tableOffset <= 0 || tableOffset + 4 > data.Length)
                return result;

            var count = BitConverter.ToInt32(data, tableOffset);
            if (count <= 0 || count > MaxChunks)
                return result;

            // The chunk table entry is four dwords in some builds and carries a trailing size field in others.
            // Rather than key off the file version, read with both strides and keep whichever recovers more
            // node chunks — a wrong stride drifts off the entries and finds almost nothing.
            foreach (var stride in ChunkEntryStrides)
            {
                var candidate = ScanNodes(data, tableOffset, count, stride);
                if (candidate.Count > result.Count)
                    result = candidate;
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"CgfHelperReader: failed reading {sourceName}: {ex.Message}");
        }

        return result;
    }

    private static readonly int[] ChunkEntryStrides = [16, 20];

    private static Dictionary<string, Vector3> ScanNodes(byte[] data, int tableOffset, int count, int stride)
    {
        var found = new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < count; i++)
        {
            var entry = tableOffset + 4 + i * stride;
            if (entry + 16 > data.Length)
                break;

            var chunkType = BitConverter.ToInt32(data, entry);
            if ((chunkType & 0xFFFF) != ChunkTypeNodeLow16)
                continue;

            var chunkOffset = BitConverter.ToInt32(data, entry + 8);
            if (chunkOffset <= 0 || chunkOffset >= data.Length)
                continue;

            // The chunk may or may not repeat its header before the node description; take whichever
            // start yields a readable name.
            if (!TryReadNode(data, chunkOffset + ChunkHeaderLength, out var name, out var pos) &&
                !TryReadNode(data, chunkOffset, out name, out pos))
                continue;

            if (!name.StartsWith('$'))
                continue;
            if (name.StartsWith("$lod", StringComparison.OrdinalIgnoreCase))
                continue;

            found[name] = pos / CentimetresPerMetre;
        }

        return found;
    }

    private static bool TryReadNode(byte[] data, int start, out string name, out Vector3 pos)
    {
        name = null;
        pos = Vector3.Zero;

        // name[64] + 4 ids + 2 bools/2 pad + 16 floats
        if (start < 0 || start + NameLength + 16 + 4 + 64 > data.Length)
            return false;

        var end = start;
        var limit = start + NameLength;
        while (end < limit && data[end] != 0)
            end++;

        var length = end - start;
        if (length is 0 or NameLength)
            return false;

        for (var i = start; i < end; i++)
        {
            if (data[i] < 0x20 || data[i] > 0x7E)
                return false;
        }

        name = Encoding.ASCII.GetString(data, start, length);

        var matrix = start + NameLength + 16 + 4;
        pos = new Vector3(
            BitConverter.ToSingle(data, matrix + 12 * 4),
            BitConverter.ToSingle(data, matrix + 13 * 4),
            BitConverter.ToSingle(data, matrix + 14 * 4));

        return float.IsFinite(pos.X) && float.IsFinite(pos.Y) && float.IsFinite(pos.Z);
    }
}
