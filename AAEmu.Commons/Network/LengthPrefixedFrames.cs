namespace AAEmu.Commons.Network;

/// <summary>
/// Result of slicing one length-prefixed internal/login frame.
/// </summary>
public enum LengthPrefixedFrameResult
{
    /// <summary>Not enough bytes for a complete frame; stash the stream and wait.</summary>
    NeedMore,
    /// <summary>A complete frame is in <c>frame</c>; remainder stays in the source stream (or null).</summary>
    GotFrame,
    /// <summary>
    /// A complete 2-byte length prefix was present but payload length was below the minimum
    /// (typically 2 for opcode). The prefix was dropped; remainder stays in the source stream.
    /// </summary>
    DroppedInvalidLength
}

/// <summary>
/// Shared [u16 payloadLen][payload] framing. PacketStream short-reads return 0 instead of throwing,
/// so callers must not treat a failed length read as payloadLen=0 (that dispatches opcode 0 forever).
/// </summary>
public static class LengthPrefixedFrames
{
    public const int LengthPrefixBytes = sizeof(ushort);
    /// <summary>Minimum payload so the frame can carry a u16 opcode.</summary>
    public const int MinOpcodePayloadBytes = sizeof(ushort);

    /// <summary>
    /// Take one frame from <paramref name="stream"/>. On <see cref="LengthPrefixedFrameResult.NeedMore"/>
    /// the same instance should be stored as LastPacket (Pos is reset to 0).
    /// </summary>
    public static LengthPrefixedFrameResult TryTake(
        ref PacketStream? stream,
        int minPayloadBytes,
        out PacketStream? frame)
    {
        frame = null;
        if (stream == null || stream.Count == 0)
            return LengthPrefixedFrameResult.NeedMore;

        stream.Pos = 0;
        if (stream.Count < LengthPrefixBytes)
            return LengthPrefixedFrameResult.NeedMore;

        var payloadLen = stream.ReadUInt16();
        if (payloadLen < minPayloadBytes)
        {
            SliceConsumed(ref stream, LengthPrefixBytes);
            return LengthPrefixedFrameResult.DroppedInvalidLength;
        }

        var total = payloadLen + LengthPrefixBytes;
        if (total > stream.Count)
        {
            stream.Pos = 0;
            return LengthPrefixedFrameResult.NeedMore;
        }

        frame = new PacketStream();
        frame.Replace(stream, 0, total);
        SliceConsumed(ref stream, total);
        return LengthPrefixedFrameResult.GotFrame;
    }

    private static void SliceConsumed(ref PacketStream? stream, int consumed)
    {
        if (stream == null)
            return;
        if (stream.Count > consumed)
        {
            var rest = new PacketStream();
            rest.Replace(stream, consumed, stream.Count - consumed);
            stream = rest;
            return;
        }

        stream = null;
    }
}
