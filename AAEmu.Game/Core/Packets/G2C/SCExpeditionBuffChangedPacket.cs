using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Per-purchase buff-grade change notice. Wire format confirmed 2026-08-28 from the client's
/// serializer FUN_39c597b0: two generic-id fields via the 4-byte slot 0x80, then named
/// "beforeGrade" and "nextGrade" (slot 0xa0, 4-byte) - i.e. four 4-byte fields. Field semantics
/// follow the family's lead-with-expeditionId convention (Buffs/MemberList/RolePolicyList):
/// [expeditionId][buffId][beforeGrade][nextGrade].
/// </summary>
public class SCExpeditionBuffChangedPacket(int expeditionId, int buffId, uint beforeGrade, uint nextGrade) : GamePacket(SCOffsets.SCExpeditionBuffChangedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(expeditionId);
        stream.Write(buffId);
        stream.Write(beforeGrade);
        stream.Write(nextGrade);
        return stream;
    }
}
