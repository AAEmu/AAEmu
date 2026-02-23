using System;
using System.IO;

namespace CgfConverter.CryEngineCore.Chunks;

internal sealed class ChunkSourceInfo_1 : ChunkSourceInfo
{
    public override void Read(BinaryReader reader)
    {
        // New World chr files
        ChunkType = _header.ChunkType;
        VersionRaw = _header.Version;
        Offset = _header.Offset;
        ID = _header.ID;
        Size = _header.Size;

        reader.BaseStream.Seek(_header.Offset, SeekOrigin.Begin);
        SourceFile = reader.ReadCString();
    }
}
