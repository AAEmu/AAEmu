using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCItemSocketingLunastoneResultPacket(bool result, ulong itemId, uint type)
    : GamePacket(SCOffsets.SCItemSocketingLunastoneResultPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(result);
        stream.Write(itemId);
        stream.Write(type);
        return stream;
    }
}
