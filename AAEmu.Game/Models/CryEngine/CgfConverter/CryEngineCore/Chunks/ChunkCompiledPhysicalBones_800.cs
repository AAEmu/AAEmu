using System.IO;

namespace CgfConverter.CryEngineCore;

internal sealed class ChunkCompiledPhysicalBones_800 : ChunkCompiledPhysicalBones
{
    public override void Read(BinaryReader b)
    {
        base.Read(b);
        SkipBytes(b, 32);  // Padding between the chunk header and the first bone.
        NumBones = (int)((Size - 32) / 152);

        for (uint i = 0; i < NumBones; i++)
        {
            // Start reading at the root bone.  First bone found is root, then read until no more bones.
            CompiledPhysicalBone tmpBone = new CompiledPhysicalBone();
            tmpBone.ReadCompiledPhysicalBone(b);
            // Set root bone if not already set
            if (RootPhysicalBone != null)
            {
                RootPhysicalBone = tmpBone;
            }
            PhysicalBoneList.Add(tmpBone);
            PhysicalBoneDictionary[i] = tmpBone;
        }

        // Add the ChildID to the parent bone.  This will help with navigation. Also set up the TransformSoFar
        foreach (CompiledPhysicalBone bone in PhysicalBoneList)
        {
            AddChildIDToParent(bone);
        }
    }
}
