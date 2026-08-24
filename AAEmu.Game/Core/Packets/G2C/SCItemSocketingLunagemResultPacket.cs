using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Result of seating or clearing a lunagem. The client calls it <c>SCItemSocketingResultPacket</c>.
/// </summary>
/// <remarks>
/// Field order and widths come from the client's own serializer (x2game.dll rva 0xa9c530), which
/// names each value as it writes it: <c>result</c> (u8), <c>itemId</c> (u64), <c>type</c> (u32),
/// <c>kind</c> (u8) and <c>success</c> (bool).
/// <para>
/// <c>kind</c> was missing. Sending four fields put the install flag where the client reads
/// <c>kind</c> and left it reading <c>success</c> off the end of the packet - which is why the gear
/// window never came back for a second attempt.
/// </para>
/// </remarks>
public class SCItemSocketingLunagemResultPacket(byte result, ulong itemId, uint type, byte kind, bool success)
    : GamePacket(SCOffsets.SCItemSocketingResultPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(result);
        stream.Write(itemId);
        stream.Write(type);
        stream.Write(kind);
        stream.Write(success);
        return stream;
    }
}
