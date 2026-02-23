using System;

namespace CgfConverter.CryEngineCore;

public abstract class ChunkTimingFormat : Chunk  // cccc000e:  Timing format chunk
{
    // This chunk doesn't have an ID, although one may be assigned in the chunk table.
    public float SecsPerTick;
    public int TicksPerFrame;
    public RangeEntity GlobalRange;
    public int NumSubRanges;

    public override string ToString() => $@"Chunk Type: {ChunkType}, ID: {ID:X}, Version: {Version}, Ticks per Frame: {TicksPerFrame}, Seconds per Tick: {SecsPerTick}";
}
