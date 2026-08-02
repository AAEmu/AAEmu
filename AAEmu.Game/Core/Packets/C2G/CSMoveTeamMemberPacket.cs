using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <remarks>
/// for an empty endpoint and only sets ghostSwap when one endpoint is empty.
/// </remarks>
public class CSMoveTeamMemberPacket() : GamePacket(CSOffsets.CSMoveTeamMemberPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var teamId = stream.ReadInt32(); // Native wire name: tid.
        var memberId = stream.ReadUInt64(); // Native wire name: type.
        var otherMemberId = stream.ReadUInt64(); // Native wire name: type.
        var memberIndex = stream.ReadSByte();
        var otherIndex = stream.ReadSByte();
        var ghostSwap = stream.ReadBoolean();

        TeamManager.Instance.MoveTeamMember(
            Connection.ActiveChar,
            teamId,
            memberId,
            otherMemberId,
            memberIndex,
            otherIndex,
            ghostSwap);
    }
}
