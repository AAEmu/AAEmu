using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Delta notification for the character sheet's game-point table: a u8 entry count, then one
/// {u8 kind, u32 amount} pair per changed point. Display-only - SCCharacterGamePointsPacket carries
/// the values, this is what makes an already-open window repaint.
///
/// The count is not optional: the client resizes its entry list to it and then reads that many pairs,
/// so a packet without one makes the client eat the kind byte as the count. Choosing the count as 1
/// makes the kind byte its own, which is what the earlier layout got wrong.
///
/// The kind is a slot index in the client's game-point table, not a currency id:
///   0 honour, 1 vocation, 11 leadership (current period), 12 leadership (previous period).
/// 2..7 and 13 also land on the honour slot, 8..10 on vocation. The client raises
/// PLAYER_HONOR_POINT for the honour slot and PLAYER_LIVING_POINT for vocation, which is what the
/// honour and vocation rows of the character sheet listen for.
/// </summary>
public class SCGamePointChangedPacket(byte kind, int amount) : GamePacket(SCOffsets.SCGamePointChangedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((byte)1); // entry count
        stream.Write(kind);
        stream.Write(amount);
        return stream;
    }
}
