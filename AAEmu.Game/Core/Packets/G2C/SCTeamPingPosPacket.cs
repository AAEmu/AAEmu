using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Team;
using AAEmu.Game.Models.Game.World.Transform;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCTeamPingPosPacket : GamePacket
{
    private readonly TeamPingPos _teamPingPos;

    public SCTeamPingPosPacket(TeamPingPos teamPingPos) : base(SCOffsets.SCTeamPingPosPacket, 5)
    {
        _teamPingPos = teamPingPos;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_teamPingPos);
        return stream;
    }
}
