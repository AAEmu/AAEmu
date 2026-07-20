using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Team;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCTeamMemberRoleChangedPacket(uint teamId, uint memberId, MemberRole role)
    : GamePacket(SCOffsets.SCTeamMemberRoleChangedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(teamId);
        stream.Write(memberId);
        stream.Write((byte)role);
        return stream;
    }
}
