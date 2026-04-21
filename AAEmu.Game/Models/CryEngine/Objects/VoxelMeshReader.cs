using System.Drawing;
using System.Numerics;

namespace AAEmu.Game.Models.CryEngine.Objects;

public class VoxelMeshReader(System.IO.Stream DecompressedModelData)
{
    public int FileType { get; private set; }
    public int FileVersion { get; private set; }
    public int EntrySize { get; private set; }
    public int NumEntries { get; private set; }
    public Dictionary<int, VoxelChunkEntry> ChunkTable { get; private set; } = [];
    public List<byte[]> ChunksData { get; private set; } = [];

    // Parsed Data
    public List<Vector3> Vertices { get; set; } = [];
    public List<Vector3> Normals { get; set; } = [];
    public List<ushort> Indices { get; set; } = [];
    public List<Color> ColorData { get; set; } = [];
    public List<ushort> SurfaceIds { get; set; } = [];
    public List<(int, List<byte[]>)> OtherChunkData { get; set; } = [];

    public bool Parse()
    {
        DecompressedModelData.Seek(0, SeekOrigin.Begin);
        try
        {
            using (var br = new BinaryReader(DecompressedModelData))
            {
                // Header
                _ = br.ReadBytes(8); // CryEngine file header
                FileType = br.ReadInt32();
                FileVersion = br.ReadInt32();

                // Chunk Table
                EntrySize = br.ReadInt32();
                NumEntries = br.ReadInt32();
                var tableByteSize = EntrySize * NumEntries;
                var rawChunkBytes = br.ReadBytes(tableByteSize);
                if (rawChunkBytes.Length != tableByteSize)
                {
                    // Error reading chunk table.
                    return false;
                }
                ChunkTable.Clear();
                for (var i = 0; i < NumEntries; i++)
                {
                    var entryOffset = i * EntrySize;
                    var chunk = new VoxelChunkEntry()
                    {                        
                        ChunkType = BitConverter.ToInt32(rawChunkBytes, entryOffset),
                        ChunkVersion = BitConverter.ToInt32(rawChunkBytes, entryOffset + 4),
                        ChunkOffset = BitConverter.ToInt32(rawChunkBytes, entryOffset + 8),
                        ChunkId = BitConverter.ToInt32(rawChunkBytes, entryOffset + 12),
                        ChunkSize = BitConverter.ToInt32(rawChunkBytes, entryOffset + 16)
                    };
                    ChunkTable.Add(i, chunk);
                }

                // Read chunks
                ChunksData.Clear();
                foreach (var (_, chunk) in ChunkTable)
                {
                    br.BaseStream.Seek(chunk.ChunkOffset, SeekOrigin.Begin);
                    var chunkData = br.ReadBytes(chunk.ChunkSize);
                    if (chunkData.Length != chunk.ChunkSize)
                    {
                        // Error reading chunk data.
                        return false;
                    }
                    ChunksData.Add(chunkData);
                }
            }

            return true;
        }
        catch
        {
            // Error reading voxel mesh data.
            return false;
        }
    }
}

public struct VoxelChunkEntry
{
    public int ChunkType;
    public int ChunkVersion;
    public int ChunkOffset;
    public int ChunkId;
    public int ChunkSize;
}
