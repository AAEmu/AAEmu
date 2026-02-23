using CgfConverter.Models;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CgfConverter.CryEngineCore;

internal sealed class ChunkCompiledBones_900 : ChunkCompiledBones
{
    public override void Read(BinaryReader b)
    {
        base.Read(b);
        NumBones = b.ReadInt32();

        for (int i = 0; i < NumBones; i++)
        {
            CompiledBone tempBone = new();
            tempBone.ReadCompiledBone_900(b);

            if (RootBone is null)  // First bone read is root bone
                RootBone = tempBone;

            BoneList.Add(tempBone);
        }

        List<string> boneNames = GetNullSeparatedStrings(NumBones, b);

        // Post bone read setup.  Parents, children, etc.
        // Add the ChildID to the parent bone.  This will help with navigation.
        for (int i = 0; i < NumBones; i++)
        {
            BoneList[i].boneName = boneNames[i];
            if (BoneList[i].offsetParent != -1)
            {
                BoneList[i].ParentBone = BoneList[BoneList[i].offsetParent];
                BoneList[i].parentID = BoneList[i].offsetParent;
                BoneList[i].ParentBone.childIDs.Add(i);
                BoneList[i].ParentBone.numChildren++;
            }
        }

        SkinningInfo skin = GetSkinningInfo();
        skin.CompiledBones = new List<CompiledBone>();
        skin.CompiledBones = BoneList;
    }

    internal static List<string> GetNullSeparatedStrings(int numberOfNames, BinaryReader b)
    {
        List<string> names = new();
        StringBuilder builder = new();

        for (int i = 0; i < numberOfNames; i++)
        {    
            char c = b.ReadChar();
            while (c != 0)
            {
                builder.Append(c);
                c = b.ReadChar();
            }
            names.Add(builder.ToString());
            builder.Clear();
        }

        return names;
    }
}
