namespace CgfConverter.CryEngineCore;

public abstract class ChunkSourceInfo : Chunk  // cccc0013:  Source Info chunk.
{
    public string SourceFile;
    public string Date;
    public string Author;

    public override string ToString()
    {
        return $@"Chunk Type: {ChunkType}, ID: {ID:X}, Sourcefile: {SourceFile}";
    }
}
