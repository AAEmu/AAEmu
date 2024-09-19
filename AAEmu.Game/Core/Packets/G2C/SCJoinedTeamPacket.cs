using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Team;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCJoinedTeamPacket : GamePacket
{
    private readonly Team _team;

    public SCJoinedTeamPacket(Team team) : base(SCOffsets.SCJoinedTeamPacket, 5)
    {
        _team = team;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_team);
        foreach (var member in _team.Members)
        {
            if (member?.Character == null)
                continue;
            stream.Write(member);
        }
        stream.Write(true); // isMine

        return stream;
    }
}
