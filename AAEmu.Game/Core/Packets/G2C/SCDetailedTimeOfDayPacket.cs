using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCDetailedTimeOfDayPacket(
    float time,
    float speed = 0.0016666f,
    float start = 0f,
    float end = 24f) : GamePacket(SCOffsets.SCDetailedTimeOfDayPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(time);
        stream.Write(speed);
        stream.Write(start);
        stream.Write(end);
        return stream;
    }
}
