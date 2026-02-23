using CgfConverter.Models;
using System.Collections.Generic;
using System.IO;

namespace CgfConverter.CryEngineCore;

internal sealed class ChunkCompiledBones_800 : ChunkCompiledBones
{
    public override void Read(BinaryReader b)
    {
        base.Read(b);
        SkipBytes(b, 32);  // Padding between the chunk header and the first bone.

        //  Read the first bone with ReadCompiledBone, then recursively grab all the children for each bone you find.
        //  Each bone structure is 584 bytes, so will need to seek childOffset * 584 each time, and go back.
        
        NumBones = (int)((Size - 32) / 584);

        for (int i = 0; i < NumBones; i++)
        {
            CompiledBone tempBone = new();
            tempBone.ReadCompiledBone_800(b);

            if (RootBone is null)  // First bone read is root bone
                RootBone = tempBone;

            if (tempBone.offsetParent != 0)
                tempBone.ParentBone = BoneList[i + tempBone.offsetParent];
            
            if (tempBone.ParentBone is not null)
                //tempBone.parentID = tempBone.ParentBone.ControllerID;
                tempBone.parentID = BoneList.IndexOf(tempBone) + tempBone.offsetParent;
            else
                tempBone.parentID = 0;

            BoneList.Add(tempBone);
        }

        SkinningInfo skin = GetSkinningInfo();
        skin.CompiledBones = new List<CompiledBone>();
        skin.CompiledBones = BoneList;
    }
}
