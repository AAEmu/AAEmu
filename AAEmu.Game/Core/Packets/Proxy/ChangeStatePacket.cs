using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.Proxy;

public class ChangeStatePacket(int state) : GamePacket(PPOffsets.ChangeStatePacket, 2)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(state);
        return stream;
    }
}
