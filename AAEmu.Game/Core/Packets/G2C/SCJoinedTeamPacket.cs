using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Team;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCJoinedTeamPacket(Team team) : GamePacket(SCOffsets.SCJoinedTeamPacket, 5)
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

        return stream;
    }
}
