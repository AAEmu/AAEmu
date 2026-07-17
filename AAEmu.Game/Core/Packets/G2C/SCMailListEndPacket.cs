using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCMailListEndPacket(int totalHeaders, int totalBodies) : GamePacket(SCOffsets.SCMailListEndPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(totalHeaders);
        stream.Write(totalBodies);
        return stream;
    }
}
