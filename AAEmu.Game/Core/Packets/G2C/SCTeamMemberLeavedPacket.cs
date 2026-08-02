using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCTeamMemberLeavedPacket(int teamId, ulong memberId, bool kicked)
    : GamePacket(SCOffsets.SCTeamMemberLeavedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        // i32 team, u64 type, bool e.
        stream.Write(teamId);
        stream.Write(memberId);
        stream.Write(kicked);
        return stream;
    }
}
