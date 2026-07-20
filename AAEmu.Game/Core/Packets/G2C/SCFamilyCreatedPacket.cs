using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCFamilyCreatedPacket(Family family) : GamePacket(SCOffsets.SCFamilyCreatedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(family);
        return stream;
    }
}
