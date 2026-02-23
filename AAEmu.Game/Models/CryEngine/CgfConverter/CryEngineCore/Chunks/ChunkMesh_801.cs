using Extensions;
using System.IO;

namespace CgfConverter.CryEngineCore;

internal sealed class ChunkMesh_801 : ChunkMesh
{
    public override void Read(BinaryReader b)
    {
        base.Read(b);

        Flags1 = b.ReadInt32();
        Flags2 = b.ReadInt32();
        NumVertices = b.ReadInt32();
        NumIndices = b.ReadInt32();
        NumVertSubsets = b.ReadUInt32();
        MeshSubsetsData = b.ReadInt32();           // Chunk ID of mesh subsets 
        VertsAnimID = b.ReadInt32();
        VerticesData = b.ReadInt32();
        NormalsData = b.ReadInt32();           // Chunk ID of the datastream for the normals for this mesh
        UVsData = b.ReadInt32();               // Chunk ID of the Normals datastream
        ColorsData = b.ReadInt32();
        Colors2Data = b.ReadInt32();
        IndicesData = b.ReadInt32();
        TangentsData = b.ReadInt32();
        SkipBytes(b, 16);
        for (int i = 0; i < 4; i++)
        {
            PhysicsData[i] = b.ReadInt32();
        }
        VertsUVsData = b.ReadInt32();          // This should be a vertsUV Chunk ID.
        ShCoeffsData = b.ReadInt32();
        ShapeDeformationData = b.ReadInt32();
        BoneMapData = b.ReadInt32();
        FaceMapData = b.ReadInt32();
        MinBound = b.ReadVector3();
        MaxBound = b.ReadVector3();
    }
}
