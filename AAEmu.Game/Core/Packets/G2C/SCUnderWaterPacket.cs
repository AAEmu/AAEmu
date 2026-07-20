using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCUnderWaterPacket(bool start) : GamePacket(SCOffsets.SCUnderWaterPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(start);
        return stream;
    }
}
