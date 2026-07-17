using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCSnowingEverywherePacket(bool on) : GamePacket(SCOffsets.SCSnowingEverywherePacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(on);
        return stream;
    }
}
