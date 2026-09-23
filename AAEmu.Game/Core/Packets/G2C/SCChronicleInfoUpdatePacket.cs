using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Chronicle status edge: GF-W13 sends it for saga group status changes, GF-W14 for milestone
/// status changes — the 10.0.2.13 corpus carries no milestone-specific packet family, so both
/// reuse this one, <c>type</c> naming the record the edge belongs to.
/// </summary>
/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 client's serializer, which passes each
/// value's name alongside the value: s8 prevStatus, s8 curStatus, s32 type.
/// </remarks>
public class SCChronicleInfoUpdatePacket(sbyte prevStatus, sbyte curStatus, int @type) : GamePacket(SCOffsets.SCChronicleInfoUpdatePacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(prevStatus);
        stream.Write(curStatus);
        stream.Write(@type);
        return stream;
    }
}
