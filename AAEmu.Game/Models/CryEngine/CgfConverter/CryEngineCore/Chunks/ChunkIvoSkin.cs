using CgfConverter.Models;

namespace CgfConverter.CryEngineCore;

public class ChunkIvoSkin : Chunk
{
    public GeometryInfo geometryInfo;
    public ChunkMesh meshChunk;
    public ChunkMeshSubsets meshSubsetsChunk;
    public ChunkDataStream indices;
    public ChunkDataStream vertsUvs;
    public ChunkDataStream tangents; 
    public ChunkDataStream colors;
    public ChunkDataStream colors2;
}
