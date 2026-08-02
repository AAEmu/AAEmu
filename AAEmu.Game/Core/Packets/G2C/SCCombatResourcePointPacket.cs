using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Combat resource total for a unit — what drives the combo-pip UI.
/// </summary>
/// <remarks>
/// 39c654a0. Slot sequence 0x40,0x40,0x1a8,0x1a0,0x80,0x98,0x80: the two 0x40s are the direction probe and
/// carry no wire field, the 0x1a8/0x1a0 pair is the write/read side of one 3-byte compressed object id, and
/// the remaining three are i32, u64 "point" and i32 "updateTime".
/// </remarks>
public class SCCombatResourcePointPacket(uint objId, int combatResourceId, ulong point, int updateTime)
    : GamePacket(SCOffsets.SCCombatResourcePointPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(objId);
        stream.Write(combatResourceId);
        stream.Write(point);
        stream.Write(updateTime);
        return stream;
    }
}
