using Extensions;
using System.IO;

namespace CgfConverter.CryEngineCore;

internal sealed class ChunkMesh_900 : ChunkMesh
{
    public override void Read(BinaryReader b)
    {
        Flags1 = 0;
        Flags2 = b.ReadInt32();
        NumVertices = b.ReadInt32();
        NumIndices = b.ReadInt32();
        NumVertSubsets = b.ReadUInt32();
        SkipBytes(b, 4);
        MinBound = b.ReadVector3();
        MaxBound = b.ReadVector3();

        ID = 2; // Node chunk ID = 1

        IndicesData = 4;
        VertsUVsData = 5;
        NormalsData = 6;
        TangentsData = 7;
        BoneMapData = 8;
        ColorsData = 9;
    }
}
