using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCSpecialtyRatioPacket(int ratio) : GamePacket(SCOffsets.SCSpecialtyRatioPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(ratio);
        return stream;
    }
}
