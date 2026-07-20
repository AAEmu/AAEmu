using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCTeamMemberMovedPacket(uint teamId, uint idFrom, uint idTo, byte from, byte to)
    : GamePacket(SCOffsets.SCTeamMemberMovedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(teamId);
        stream.Write(idFrom);
        stream.Write(idTo);
        stream.Write(from);
        stream.Write(to);
        return stream;
    }
}
