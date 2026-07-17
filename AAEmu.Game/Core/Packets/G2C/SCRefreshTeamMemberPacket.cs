using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCRefreshTeamMemberPacket(uint teamId, uint memberId, uint objId)
    : GamePacket(SCOffsets.SCRefreshTeamMemberPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(teamId);
        stream.Write(memberId);
        stream.WriteBc(objId);
        return stream;
    }
}
