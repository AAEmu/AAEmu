using System;
using System.IO;

namespace CgfConverter.CryEngineCore;

internal sealed class ChunkMtlName_900 : ChunkMtlName
{
    public override void Read(BinaryReader b)
    {
        base.Read(b);
        Name = b.ReadFString(128);
        NumChildren = 0;
    }
}
