using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Team;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCTeamMemberDisconnectedPacket(uint teamId, uint id, TeamMember member)
    : GamePacket(SCOffsets.SCTeamMemberDisconnectedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(teamId);
        stream.Write(id);
        member.WritePerson(stream);
        return stream;
    }
}
