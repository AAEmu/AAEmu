using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSKickTeamMemberPacket() : GamePacket(CSOffsets.CSKickTeamMemberPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        // i32 tid, u64 type.
        var teamId = stream.ReadInt32();
        var memberId = stream.ReadUInt64();

        TeamManager.Instance.KickTeamMember(Connection.ActiveChar, teamId, memberId);
    }
}
