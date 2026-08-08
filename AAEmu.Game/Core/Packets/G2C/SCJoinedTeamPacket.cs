using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Team;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Read by the client at VA 0x39C7ED00 as exactly three things:
///
///   team header          (see <see cref="Team.Write"/>)
///   member x N           (see <see cref="TeamMember.Write"/>)
///   isMine   bool  1
///
/// N is NOT a length prefix - the client derives it by summing the ten per-party "num" counters in the
/// header, then reads that many members:
///
///     ecx = [this+0x21] + [this+0x22] + ... + [this+0x2a]
///     while (i &lt; ecx) ReadMember(...)
///
/// So the number of members appended here has to match GetPartyCounts()'s total exactly. It does:
/// both count members whose Character is non-null.
/// </summary>
public class SCJoinedTeamPacket(Team team, bool isMine = true) : GamePacket(SCOffsets.SCJoinedTeamPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(team);
        foreach (var member in team.Members)
        {
            if (member?.Character == null)
                continue;
            stream.Write(member);
        }

        // The trailing flag was missing entirely. It tells the client this team is its own.
        stream.Write(isMine);   // bool isMine

        return stream;
    }
}
