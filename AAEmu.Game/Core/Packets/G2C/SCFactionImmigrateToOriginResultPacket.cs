using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCFactionImmigrateToOriginResultPacket(string charName, uint id)
    : GamePacket(SCOffsets.SCFactionImmigrateToOriginResultPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(charName);
        stream.Write(id);
        return stream;
    }
}
