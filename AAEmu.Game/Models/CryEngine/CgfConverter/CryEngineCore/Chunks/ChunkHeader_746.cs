using System.IO;

namespace CgfConverter.CryEngineCore;

internal sealed class ChunkHeader_746 : ChunkHeader
{
    public override void Read(BinaryReader reader)
    {
        uint headerType = reader.ReadUInt16() + 0xCCCBF000;
        ChunkType = (ChunkType)headerType;
        VersionRaw = reader.ReadUInt16();
        ID = reader.ReadInt32();
        Size = reader.ReadUInt32();
        Offset = reader.ReadUInt32();
    }
}
