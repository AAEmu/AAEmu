using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Team;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCTeamAckRiskyActionPacket(uint teamId, uint id, RiskyAction ra, int w, short errorMessage)
    : GamePacket(SCOffsets.SCTeamAckRiskyActionPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(teamId);
        stream.Write(id);
        stream.Write((byte)ra);
        stream.Write(w);
        stream.Write(errorMessage);
        return stream;
    }
}
