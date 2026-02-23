namespace CgfConverter.CryEngineCore;

public abstract class ChunkCompiledIntSkinVertices : Chunk
{
    public int Reserved;
    public IntSkinVertex[]? IntSkinVertices;
    public int NumIntVertices { get; set; }                  // Calculate by size of data div by size of IntSkinVertex structure.

    public override string ToString() => $@"Chunk Type: {ChunkType}, ID: {ID:X}";
}
