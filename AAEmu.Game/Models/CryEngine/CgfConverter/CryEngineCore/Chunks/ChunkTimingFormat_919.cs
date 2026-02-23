using System;
using System.IO;

namespace CgfConverter.CryEngineCore.Chunks;

internal sealed class ChunkTimingFormat_919 : ChunkTimingFormat
{
    public override void Read(BinaryReader reader)
    {
        base.Read(reader);

        // TODO:  This is copied from 918 but may not be entirely accurate.  Not tested.
        SecsPerTick = reader.ReadSingle();
        TicksPerFrame = reader.ReadInt32();
        GlobalRange.Name = reader.ReadFString(32);  // Name is technically a String32
        GlobalRange.Start = reader.ReadInt32();
        GlobalRange.End = reader.ReadInt32();
    }
}
