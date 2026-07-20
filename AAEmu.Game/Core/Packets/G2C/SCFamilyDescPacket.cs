using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCFamilyDescPacket(Family family) : GamePacket(SCOffsets.SCFamilyDescPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(family);
        return stream;
    }
}
