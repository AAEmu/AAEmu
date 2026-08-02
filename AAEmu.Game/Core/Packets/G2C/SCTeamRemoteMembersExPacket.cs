using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Team;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCTeamRemoteMembersExPacket(int teamId, IReadOnlyList<TeamMember> members) : GamePacket(SCOffsets.SCTeamRemoteMembersExPacket, 1)
{
    private const int NativeMemberLimit = 50;

    public override PacketStream Write(PacketStream stream)
    {
        // i32 teamId, i32 count (clamped to 50), RemoteMember[count].
        stream.Write(teamId);
        var count = Math.Min(members.Count, NativeMemberLimit);
        stream.Write(count);
        for (var i = 0; i < count; i++)
            members[i].WritePerson(stream);
        return stream;
    }
}
