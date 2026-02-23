using System;
using System.IO;

namespace CgfConverter.CryEngineCore;

internal sealed class ChunkExportFlags_1 : ChunkExportFlags
{
    public override void Read(BinaryReader b)
    {
        base.Read(b);

        uint tmpExportFlag = b.ReadUInt32();
        ChunkType = (ChunkType)tmpExportFlag;
        VersionRaw = b.ReadUInt32();
        ChunkOffset = b.ReadUInt32();
        ID = b.ReadInt32();

        SkipBytes(b, 4);

        RCVersion = new uint[4];
        for (int count = 0; count < 4; count++)
        {
            RCVersion[count] = b.ReadUInt32();
        }
        RCVersionString = b.ReadFString(16);

        SkipBytes(b);
    }
}
