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
public class SCExpdWarHistoriesPacket(uint historiesCount, int @type, string declarerName, int @type2, string defendantName, long declareDate, uint declarerKills, uint defendantKills) : GamePacket(SCOffsets.SCExpdWarHistoriesPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(historiesCount);
        stream.Write(@type);
        stream.Write(declarerName);
        stream.Write(@type2);
        stream.Write(defendantName);
        stream.Write(declareDate);
        stream.Write(declarerKills);
        stream.Write(defendantKills);
        return stream;
    }
}
