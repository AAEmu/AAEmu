using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Team;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Client layout (VA 0x39C72430): tid u32, type u64, then the same "person" block
/// <see cref="SCTeamRemoteMembersExPacket"/> uses. The member id is EIGHT bytes.
/// </summary>
public class SCTeamMemberDisconnectedPacket(uint teamId, ulong id, TeamMember member)
    : GamePacket(SCOffsets.SCTeamMemberDisconnectedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(teamId);       // u32 tid
        stream.Write(id);           // u64 type
        member.WritePerson(stream);
        return stream;
    }
}
