using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCAboxTeleportPacket(int x, int y) : GamePacket(SCOffsets.SCAboxTeleportPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(x);
        stream.Write(y);
        return stream;
    }
}
