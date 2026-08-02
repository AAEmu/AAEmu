using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 client's serializer, which passes each
/// value's name alongside the value:
/// </remarks>
public class SCExpeditionMemberListEndPacket(int total, int expeditionId) : GamePacket(SCOffsets.SCExpeditionMemberListEndPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(total);
        stream.Write(expeditionId);
        return stream;
    }
}
