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
public class SCExpeditionSummonSuggestPacket(string name, uint zoneId, float posX, float posY, float posZ) : GamePacket(SCOffsets.SCExpeditionSummonSuggestPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(name);
        stream.Write(zoneId);
        stream.Write(posX);
        stream.Write(posY);
        stream.Write(posZ);
        return stream;
    }
}
