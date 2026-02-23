namespace CgfConverter.CryEngineCore;

public abstract class ChunkCompiledIntFaces : Chunk
{
    public int Reserved;
    public uint NumIntFaces;
    public TFace[]? Faces;

    public override string ToString() => $@"Chunk Type: {ChunkType}, ID: {ID:X}";
}
