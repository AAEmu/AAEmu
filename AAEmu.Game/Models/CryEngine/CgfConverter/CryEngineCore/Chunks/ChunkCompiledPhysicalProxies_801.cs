using System.IO;

namespace CgfConverter.CryEngineCore.Chunks;

internal class ChunkCompiledPhysicalProxies_801 : ChunkCompiledPhysicalProxies
{
    public override void Read(BinaryReader b)
    {
        base.Read(b);
        // Not used in any of the current renderers
    }
}
