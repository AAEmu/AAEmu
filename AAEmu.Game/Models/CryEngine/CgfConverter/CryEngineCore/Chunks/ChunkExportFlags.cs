using System;

namespace CgfConverter.CryEngineCore
{
    public abstract class ChunkExportFlags : Chunk  // cccc0015:  Export Flags
    {
        public uint ChunkOffset;  // for some reason the offset of Export Flag chunk is stored here.
        public uint Flags;    // ExportFlags type technically, but it's just 1 value
        public uint[] RCVersion;  // 4 uints
        public String RCVersionString;  // Technically String16

        public override string ToString()
        {
            return $@"Chunk Type: {ChunkType}, ID: {ID:X}, Version: {Version}";
        }
    }
}
