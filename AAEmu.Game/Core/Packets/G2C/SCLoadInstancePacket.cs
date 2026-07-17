using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCLoadInstancePacket(
    uint instanceId,
    uint zoneId,
    float x,
    float y,
    float z,
    float angX,
    float angY,
    float angZ)
    : GamePacket(SCOffsets.SCLoadInstancePacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(instanceId);
        stream.Write(zoneId);
        stream.Write(x);
        stream.Write(y);
        stream.Write(z);
        stream.Write(angX);
        stream.Write(angY);
        stream.Write(angZ);
        return stream;
    }
}
