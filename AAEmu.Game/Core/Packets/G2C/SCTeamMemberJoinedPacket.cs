using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Team;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCTeamMemberJoinedPacket(uint teamId, TeamMember member, int party)
    : GamePacket(SCOffsets.SCTeamMemberJoinedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(teamId);
        stream.Write(member);
        stream.Write(party);
        return stream;
    }
}
