namespace CgfConverter.CryEngineCore;

public abstract class ChunkCompiledExtToIntMap : Chunk
{
    public int Reserved;
    public uint NumExtVertices;
    public ushort[]? Source;

    public override string ToString() => $@"Chunk Type: {ChunkType}, ID: {ID:X}";
}
