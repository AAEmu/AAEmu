using System.IO;

namespace CgfConverter.CryEngineCore;

internal sealed class ChunkHeader_744 : ChunkHeader
{
    public override void Read(BinaryReader reader)
    {
        uint headerType = reader.ReadUInt32();
        ChunkType = (ChunkType)headerType;
        VersionRaw = reader.ReadUInt32();
        Offset = reader.ReadUInt32();
        ID = reader.ReadInt32();
        Size = 0; // TODO: Figure out how to return a size - postprocess header table maybe?
    }
}
