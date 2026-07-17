using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCDetailedTimeOfDayPacket(float time) : GamePacket(SCOffsets.SCDetailedTimeOfDayPacket, 5)
{
    private readonly float _speed = 0.0016666f;
    private readonly float _start = 0f;
    private readonly float _end = 24f;

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(time);
        stream.Write(_speed);
        stream.Write(_start);
        stream.Write(_end);
        return stream;
    }
}
