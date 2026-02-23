using System.IO;

namespace CgfConverter.CryEngineCore;

internal sealed class ChunkHeader_745 : ChunkHeader
{
    public override void Read(BinaryReader reader)
    {
        uint headerType = reader.ReadUInt32();
        ChunkType = (ChunkType)headerType;
        VersionRaw = reader.ReadUInt32();
        Offset = reader.ReadUInt32();
        ID = reader.ReadInt32();
        Size = reader.ReadUInt32();
    }
}
