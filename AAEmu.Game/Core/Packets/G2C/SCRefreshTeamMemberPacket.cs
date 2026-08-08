using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Client layout (VA 0x39C79880): tid u32, type u64, bc. The member id is EIGHT bytes.
/// </summary>
public class SCRefreshTeamMemberPacket(uint teamId, ulong memberId, uint objId)
    : GamePacket(SCOffsets.SCRefreshTeamMemberPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(teamId);       // u32 tid
        stream.Write(memberId);     // u64 type
        stream.WriteBc(objId);      // bc
        return stream;
    }
}
