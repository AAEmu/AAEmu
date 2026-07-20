using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCReconnectAuthPacket(uint token) : GamePacket(SCOffsets.SCReconnectAuthPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(token);
        return stream;
    }
}
