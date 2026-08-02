using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCTeamBecameRaidTeamPacket(int teamId) : GamePacket(SCOffsets.SCTeamBecameRaidTeamPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        // i32 team.
        stream.Write(teamId);
        return stream;
    }
}
