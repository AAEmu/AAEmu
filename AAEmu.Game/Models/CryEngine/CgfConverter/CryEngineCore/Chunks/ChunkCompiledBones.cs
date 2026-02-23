using System.Collections.Generic;
using System.Linq;

namespace CgfConverter.CryEngineCore;

public abstract class ChunkCompiledBones : Chunk     //  Bones info
{
    public string RootBoneName;         // Controller ID?  Name?  Not sure yet.
    public CompiledBone RootBone;       // First bone in the data structure.
    public int NumBones;                // Number of bones in the chunk

    // Bones are a bit different than Node Chunks, since there is only one CompiledBones Chunk, and it contains all the bones in the model.
    public List<CompiledBone> BoneList = new();

    public List<CompiledBone> GetChildBones(CompiledBone bone)
    {
        List<CompiledBone> childBones = new();
        foreach (var bone1 in BoneList)
        {
            if (bone1.ParentBone == bone)
                childBones.Add(bone1);
        }
        return childBones;
    }

    public List<string> GetBoneNames() => BoneList.Select(a => a.boneName).ToList();

    public override string ToString() => $@"Chunk Type: {ChunkType}, ID: {ID:X}";
}
