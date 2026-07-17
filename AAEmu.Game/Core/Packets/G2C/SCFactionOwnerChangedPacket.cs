using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCFactionOwnerChangedPacket(uint id, uint id2, string newOwnerName)
    : GamePacket(SCOffsets.SCFactionOwnerChangedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(id);
        stream.Write(id2);
        stream.Write(newOwnerName);
        return stream;
    }
}
