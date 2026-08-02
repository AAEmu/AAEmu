using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCLeavedTeamPacket(int teamId, bool kicked, bool dismissed)
    : GamePacket(SCOffsets.SCLeavedTeamPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        // i32 team, bool e, bool d.
        stream.Write(teamId);
        stream.Write(kicked);
        stream.Write(dismissed);
        return stream;
    }
}
