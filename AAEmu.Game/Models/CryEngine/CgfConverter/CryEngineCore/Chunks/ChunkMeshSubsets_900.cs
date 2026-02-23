using Extensions;
using System.IO;

namespace CgfConverter.CryEngineCore;

internal sealed class ChunkMeshSubsets_900 : ChunkMeshSubsets
{
    public ChunkMeshSubsets_900(uint numVertSubsets)
    {
        NumMeshSubset = numVertSubsets;
    }

    public override void Read(BinaryReader b)
    {
        base.Read(b);

        MeshSubsets = new MeshSubset[NumMeshSubset];
        for (int i = 0; i < NumMeshSubset; i++)
        {
            MeshSubsets[i].MatID = b.ReadInt16();
            SkipBytes(b, 2);  // 2 unknowns; possibly padding;
            MeshSubsets[i].FirstIndex = b.ReadInt32();
            MeshSubsets[i].NumIndices = b.ReadInt32();
            MeshSubsets[i].FirstVertex = b.ReadInt32();
            MeshSubsets[i].NumVertices = b.ReadInt32();
            MeshSubsets[i].Radius = b.ReadSingle();
            MeshSubsets[i].Center = b.ReadVector3();
            SkipBytes(b, 12);  // 3 unknowns; possibly floats;
        }
    }
}
