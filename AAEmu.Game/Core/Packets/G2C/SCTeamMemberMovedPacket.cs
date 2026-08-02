using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <remarks>
/// i8 from, i8 to, and bool ghostSwap.
/// </remarks>
public class SCTeamMemberMovedPacket(int teamId, ulong memberId, ulong otherMemberId, sbyte from, sbyte to, bool ghostSwap)
    : GamePacket(SCOffsets.SCTeamMemberMovedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(teamId); // Native wire name: tid.
        stream.Write(memberId); // Native wire name: type.
        stream.Write(otherMemberId); // Native wire name: type.
        stream.Write(from);
        stream.Write(to);
        stream.Write(ghostSwap);
        return stream;
    }
}
