using Extensions;
using System.IO;

namespace CgfConverter.CryEngineCore;

internal sealed class ChunkMeshSubsets_800 : ChunkMeshSubsets
{
    public override void Read(BinaryReader b)
    {
        base.Read(b);

        Flags = b.ReadUInt32();   // Might be a ref to this chunk
        NumMeshSubset = b.ReadUInt32();  // number of mesh subsets
        SkipBytes(b, 8);
        MeshSubsets = new MeshSubset[NumMeshSubset];
        for (int i = 0; i < NumMeshSubset; i++)
        {
            MeshSubsets[i].FirstIndex = b.ReadInt32();
            MeshSubsets[i].NumIndices = b.ReadInt32();
            MeshSubsets[i].FirstVertex = b.ReadInt32();
            MeshSubsets[i].NumVertices = b.ReadInt32();
            MeshSubsets[i].MatID = b.ReadInt32();
            MeshSubsets[i].Radius = b.ReadSingle();
            MeshSubsets[i].Center = b.ReadVector3();
        }
    }
}
