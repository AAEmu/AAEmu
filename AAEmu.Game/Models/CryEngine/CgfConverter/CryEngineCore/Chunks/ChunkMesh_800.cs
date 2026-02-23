using Extensions;
using System.IO;

namespace CgfConverter.CryEngineCore;

internal sealed class ChunkMesh_800 : ChunkMesh
{
    public override void Read(BinaryReader b)
    {
        base.Read(b);

        NumVertSubsets = 1;
        SkipBytes(b, 8);
        NumVertices = b.ReadInt32();
        NumIndices = b.ReadInt32();   //  Number of indices
        SkipBytes(b, 4);
        MeshSubsetsData = b.ReadInt32();  // refers to ID in mesh subsets  1d for candle.  Just 1 for 0x800 type
        SkipBytes(b, 4);
        VerticesData = b.ReadInt32();  // ID of the datastream for the vertices for this mesh
        NormalsData = b.ReadInt32();   // ID of the datastream for the normals for this mesh
        UVsData = b.ReadInt32();     // refers to the ID in the Normals datastream?
        ColorsData = b.ReadInt32();
        Colors2Data = b.ReadInt32();
        IndicesData = b.ReadInt32();
        TangentsData = b.ReadInt32();
        ShCoeffsData = b.ReadInt32();
        ShapeDeformationData = b.ReadInt32();
        BoneMapData = b.ReadInt32();
        FaceMapData = b.ReadInt32();
        VertMatsData = b.ReadInt32();
        SkipBytes(b, 16);
        for (int i = 0; i < 4; i++)
        {
            PhysicsData[i] = b.ReadInt32();
            if (PhysicsData[i] != 0)
                MeshPhysicsData = PhysicsData[i];
        }
        MinBound = b.ReadVector3();
        MaxBound = b.ReadVector3();
    }
}
