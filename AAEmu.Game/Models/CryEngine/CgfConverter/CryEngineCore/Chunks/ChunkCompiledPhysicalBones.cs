using CgfConverter.Structs;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace CgfConverter.CryEngineCore;

public abstract class ChunkCompiledPhysicalBones : Chunk
{
    public char[] Reserved;             // 32 byte array
    public CompiledPhysicalBone RootPhysicalBone;       // First bone in the data structure.  Usually Bip01
    public int NumBones;                // Number of bones in the chunk

    public Dictionary<uint, CompiledPhysicalBone> PhysicalBoneDictionary = new Dictionary<uint, CompiledPhysicalBone>();  // Dictionary of all the CompiledBone objects based on bone name.
    public List<CompiledPhysicalBone> PhysicalBoneList = new List<CompiledPhysicalBone>();

    protected void AddChildIDToParent(CompiledPhysicalBone bone)
    {
        // Root bone parent ID will be zero.
        if (bone.parentID != 0)
        {
            CompiledPhysicalBone parent = PhysicalBoneList.Where(a => a.ControllerID == bone.parentID).FirstOrDefault();  // Should only be one parent.
            parent.childIDs.Add(bone.ControllerID);
        }
    }

    protected Matrix4x4 GetTransformFromParts(Vector3 localTranslation, Matrix3x3 localRotation)
    {
        Matrix4x4 transform = new Matrix4x4
        {
            // Translation part
            M14 = localTranslation.X,
            M24 = localTranslation.Y,
            M34 = localTranslation.Z,
            // Rotation part
            M11 = localRotation.M11,
            M12 = localRotation.M12,
            M13 = localRotation.M13,
            M21 = localRotation.M21,
            M22 = localRotation.M22,
            M23 = localRotation.M23,
            M31 = localRotation.M31,
            M32 = localRotation.M32,
            M33 = localRotation.M33,
            // Set final row
            M41 = 0,
            M42 = 0,
            M43 = 0,
            M44 = 1
        };
        return transform;
    }

    public override string ToString() => $@"Chunk Type: {ChunkType}, ID: {ID:X}";
}
