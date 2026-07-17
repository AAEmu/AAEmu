using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCTeamOwnerChangedPacket(uint teamId, uint id) : GamePacket(SCOffsets.SCTeamOwnerChangedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(teamId);
        stream.Write(id);
        return stream;
    }
}
