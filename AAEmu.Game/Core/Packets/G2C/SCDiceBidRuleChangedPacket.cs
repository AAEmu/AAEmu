using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Team;

namespace AAEmu.Game.Core.Packets.G2C;

/// <remarks>
/// </remarks>
public class SCDiceBidRuleChangedPacket(int teamId, ulong memberId, DiceBidRuleKind newKind)
    : GamePacket(SCOffsets.SCDiceBidRuleChangedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(teamId);
        stream.Write(memberId); // Native wire name: type.
        stream.Write((sbyte)newKind);
        return stream;
    }
}
