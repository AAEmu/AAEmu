namespace CgfConverter.CryEngineCore;

public abstract class ChunkCompiledPhysicalProxies : Chunk        // 0xACDC0003:  Hit boxes?
{
    // Properties.  VERY similar to datastream, since it's essential vertex info.
    public uint Flags2;
    public uint NumPhysicalProxies; // Number of data entries
    public uint BytesPerElement; // Bytes per data entry
    //public UInt32 Reserved1;
    //public UInt32 Reserved2;
    public PhysicalProxy[] PhysicalProxies;

    public override string ToString() => $@"Chunk Type: {ChunkType}, ID: {ID:X}, Number of Targets: {NumPhysicalProxies}";
}
