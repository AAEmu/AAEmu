using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCTeamDismissedPacket(uint teamId) : GamePacket(SCOffsets.SCTeamDismissedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(teamId);
        return stream;
    }
}
