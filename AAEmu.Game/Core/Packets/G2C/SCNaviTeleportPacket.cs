using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCNaviTeleportPacket(float x, float y, float z) : GamePacket(SCOffsets.SCNaviTeleportPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(Helpers.ConvertLongX(x));
        stream.Write(Helpers.ConvertLongY(y));
        stream.Write(z);
        return stream;
    }
}
