using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCAddActionPointPacket(int action, int value) : GamePacket(SCOffsets.SCAddActionPointPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(action);
        stream.Write(value);
        return stream;
    }
}
