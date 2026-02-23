using CgfConverter.Models;
using System.IO;
using System.Linq;

namespace CgfConverter.CryEngineCore;

internal sealed class ChunkCompiledExtToIntMap_800 : ChunkCompiledExtToIntMap
{
    public override void Read(BinaryReader b)
    {
        base.Read(b);
        NumExtVertices = DataSize / sizeof(ushort);
        Source = new ushort[NumExtVertices];
        for (int i = 0; i < NumExtVertices; i++)
        {
            Source[i] = b.ReadUInt16();
        }
        // Add to SkinningInfo
        SkinningInfo skin = GetSkinningInfo();
        skin.Ext2IntMap = Source.ToList();
        skin.HasIntToExtMapping = true;
    }
}
