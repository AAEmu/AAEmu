using System;
using System.IO;

namespace CgfConverter.CryEngineCore;

internal sealed class ChunkBoneNameList_745 : ChunkBoneNameList
{
    public override void Read(BinaryReader b)
    {
        base.Read(b);       

        var sizeOfList = b.ReadInt32();
        var upperOffset = Offset + Size;
        int i = 0;
        while (i < sizeOfList && b.BaseStream.Position < upperOffset)
        {
            var boneName = b.ReadCString();
            BoneNames.Add(boneName);
            i++;
        }
        if (i < sizeOfList)
            throw new Exception($"Only {i} out of {sizeOfList} bones found");
    }
}
