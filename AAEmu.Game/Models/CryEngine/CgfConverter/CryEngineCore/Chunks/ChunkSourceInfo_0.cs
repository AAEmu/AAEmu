using System;
using System.IO;

namespace CgfConverter.CryEngineCore;

internal sealed class ChunkSourceInfo_0 : ChunkSourceInfo
{
    public override void Read(BinaryReader reader)
    {
        ChunkType = _header.ChunkType;
        VersionRaw = _header.Version;
        Offset = _header.Offset;
        ID = _header.ID;
        Size = _header.Size;

        reader.BaseStream.Seek(_header.Offset, SeekOrigin.Begin);

        uint peek = reader.ReadUInt32();

        // Try and detect SourceInfo type - if it's there, we need to skip ahead a few bytes
        if ((peek == (uint)ChunkType.SourceInfo) || (peek + 0xCCCBF000 == (uint)ChunkType.SourceInfo))
            SkipBytes(reader, 12);
        else
            reader.BaseStream.Seek(_header.Offset, 0);

        if (Offset != _header.Offset || Size != _header.Size)
        {
            //Utilities.Log(LogLevelEnum.Debug, "Conflict in chunk definition:  SourceInfo chunk");
            //Utilities.Log(LogLevelEnum.Debug, "{0:X}+{1:X}", _header.Offset, _header.Size);
            //Utilities.Log(LogLevelEnum.Debug, "{0:X}+{1:X}", Offset, Size);
        }

        ChunkType = ChunkType.SourceInfo; // this chunk doesn't actually have the chunktype header.
        SourceFile = reader.ReadCString();
        Date = reader.ReadCString().TrimEnd(); // Strip off last 2 Characters, because it contains a return
        // It is possible that Date has a newline in it instead of a null.  If so, split it based on newline.  Otherwise read Author.
        if (Date.Contains('\n'))
        {
            Author = Date.Split('\n')[1];
            Date = Date.Split('\n')[0];
        }
        else
            Author = reader.ReadCString();
    }
}
