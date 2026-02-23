namespace CgfConverter.CryEngineCore;

public abstract class ChunkSceneProp : Chunk     // cccc0008 
{
    // This chunk isn't really used, but contains some data probably necessary for the game.
    // Size for 0x744 type is always 0xBB4 (test this)
    public uint NumProps;             // number of elements in the props array  (31 for type 0x744)
    public string[] PropKey;
    public string[] PropValue;

    public override string ToString()
    {
        return $@"Chunk Type: {ChunkType}, ID: {ID:X}, Version: {Version}";
    }
}
