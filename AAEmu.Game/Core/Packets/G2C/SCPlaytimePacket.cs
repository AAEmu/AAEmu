using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCPlaytimePacket(int playTime) : GamePacket(SCOffsets.SCPlaytimePacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(playTime);
        return stream;
    }
}
