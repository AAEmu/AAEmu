using System.Collections.Generic;

namespace CgfConverter.Models;

public class SkinningInfo
{
    /// <summary> If there is skinning info in the model, set to true. </summary>
    public bool HasSkinningInfo => CompiledBones is not null ? CompiledBones.Count > 0 : false;
    /// <summary> If there is an internal vertex to external vertex mapping, set to true. </summary>
    public bool HasIntToExtMapping { get; internal set; }
    /// <summary> BoneEntities are the list of the bones in the object.  Contains the info to find each of the necessary skinning components. </summary>
    public List<BoneEntity>? BoneEntities { get; set; }
    public List<CompiledBone>? CompiledBones { get; set; }
    public List<DirectionalBlends>? LookDirectionBlends { get; set; }
    public List<PhysicalProxy>? PhysicalBoneMeshes { get; set; }           // Collision proxies
    public List<MorphTargets>? MorphTargets { get; set; }
    public List<TFace>? IntFaces { get; set; }
    public List<IntSkinVertex>? IntVertices { get; set; }
    public List<ushort>? Ext2IntMap { get; set; }
    public List<MeshCollisionInfo>? Collisions { get; set; }
    public List<MeshBoneMapping>? BoneMapping { get; set; }                  // Bone Mappings are read from a Datastream chunk

    /// <summary>
    /// Given a bone name, get the bone index.
    /// </summary>
    /// <param name="jointName">Bone name</param>
    /// <returns>Index of the bone.</returns>
    public int GetBoneIndexByName(string jointName) => CompiledBones.FindIndex(joint => Equals(joint, jointName));

    /// <summary>
    /// Given a bone index, get the name of the bone.
    /// </summary>
    /// <param name="boneIndex">Index of the bone.</param>
    /// <returns>Name of the bone.</returns>
    public string GetJointNameByID(int boneIndex)
    {
        int numJoints = CompiledBones.Count;
        if (boneIndex >= 0 && boneIndex < numJoints)
            return CompiledBones[boneIndex].boneName;

        return string.Empty;        // Invalid bone ID
    }
}
