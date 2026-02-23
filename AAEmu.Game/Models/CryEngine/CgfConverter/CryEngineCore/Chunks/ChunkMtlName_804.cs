using System;
using System.IO;

namespace CgfConverter.CryEngineCore.Chunks;

internal class ChunkMtlName_804 : ChunkMtlName
{
    public override void Read(BinaryReader b)
    {
        base.Read(b);

        AssetId = Guid.Parse(b.ReadFString(38));

        SkipBytes(b, 26);

        // some count followed by 3 emtpy bytes 
        var count = b.ReadByte();
        SkipBytes(b, 3);
        SkipBytes(b, count * 4);

        Name = b.ReadCString();
        NumChildren = 0;
    }
}
