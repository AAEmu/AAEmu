using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Duels;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Client layout (read at RVA 0xC88060), which names every field it reads:
///
///   isWin           bool                    did THIS recipient win
///   det             u8                      see <see cref="DuelDetType"/>
///   opponentUnitIds u32 count, count x bc   helper at RVA 0xC87350
///   opponentCharIds u32 count, count x u64  helper at RVA 0xACD460
///
/// Both lists use the engine's standard vector encoding: a u32 "Size" followed by that many elements.
/// We used to send "u32 challengerId, u32 challengedId, bc, bc, u8 det" - fifteen bytes of a layout
/// this client never had. It read our first four bytes as isWin+det+the start of a count, then ran off
/// the end of the packet, so no result message could ever appear.
///
/// The packet is written from the recipient's point of view, so each side gets its own copy. The lists
/// are plural because a party duel has more than one opponent; a 1v1 sends one entry each.
/// </summary>
public class SCDuelEndedPacket(
    bool isWin,
    DuelDetType det,
    uint[] opponentObjIds,
    ulong[] opponentCharIds)
    : GamePacket(SCOffsets.SCDuelEndedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(isWin);        // bool isWin
        stream.Write((byte)det);    // u8   det

        stream.Write((uint)opponentObjIds.Length);      // u32 Size
        foreach (var objId in opponentObjIds)
            stream.WriteBc(objId);                      // bc  v

        stream.Write((uint)opponentCharIds.Length);     // u32 Size
        foreach (var charId in opponentCharIds)
            stream.Write(charId);                       // u64 type

        return stream;
    }
}
