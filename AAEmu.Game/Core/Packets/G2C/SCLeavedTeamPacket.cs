using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCLeavedTeamPacket(uint teamId, bool e, bool d) : GamePacket(SCOffsets.SCLeavedTeamPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(teamId);
        stream.Write(e);
        stream.Write(d);
        return stream;
    }
}
