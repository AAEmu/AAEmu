namespace CgfConverter.CryEngineCore
{
    /// <summary>
    /// Legacy class.  No longer used.
    /// </summary>
    public abstract class ChunkMeshMorphTargets : Chunk
    {
        public uint ChunkIDMesh;
        public uint NumMorphVertices;

        public override string ToString()
        {
            return $@"Chunk Type: {ChunkType}, ID: {ID:X}, Version: {Version}, Chunk ID Mesh: {ChunkIDMesh}";
        }
    }
}
