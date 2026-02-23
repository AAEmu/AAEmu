using System;
using System.IO;

namespace CgfConverter.CryEngineCore;

internal sealed class ChunkMtlName_802 : ChunkMtlName
{
    public override void Read(BinaryReader b)
    {
        // Appears to have 4 more Bytes than ChunkMtlName_744
        base.Read(b);

        Name = b.ReadFString(128);
        NumChildren = b.ReadUInt32();
        PhysicsType = new MtlNamePhysicsType[NumChildren];
        MatType = NumChildren == 0 ? MtlNameType.Single : MtlNameType.Library;

        for (int i = 0; i < NumChildren; i++)
        {
            PhysicsType[i] = (MtlNamePhysicsType)b.ReadUInt32();
        }
    }
}
