namespace CgfConverter.CryEngineCore;

public abstract class ChunkHeader : Chunk
{
    public override string ToString() => $" ChunkType: {ChunkType}, ChunkVersion: {Version:X}, Offset: {Offset:X}, ID: {ID:X}, Size: {Size:X}";
}
