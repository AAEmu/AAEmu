using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// TODO: nothing constructs this packet yet.
/// </summary>
/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 client's serializer, which passes each
/// value's name alongside the value:
/// </remarks>
public class SCDumpGachaRecordPacket(uint count, uint glpId, uint totalCount, uint galpId) : GamePacket(SCOffsets.SCDumpGachaRecordPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(count);
        stream.Write(glpId);
        stream.Write(totalCount);
        stream.Write(galpId);
        return stream;
    }
}
