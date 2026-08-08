using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Team;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Client layout (VA 0x39C773F0): tid u32, type u64, role u8. The member id is EIGHT bytes, as it is
/// in every other team packet that carries one.
/// </summary>
public class SCTeamMemberRoleChangedPacket(uint teamId, ulong memberId, MemberRole role)
    : GamePacket(SCOffsets.SCTeamMemberRoleChangedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(teamId);       // u32 tid
        stream.Write(memberId);     // u64 type
        stream.Write((byte)role);   // u8  role
        return stream;
    }
}
