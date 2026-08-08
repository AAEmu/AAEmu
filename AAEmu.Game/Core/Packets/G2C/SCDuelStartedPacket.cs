using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Client layout (read at RVA 0xC5B5B0): a single bc, then duelType u8 - four bytes, not six.
/// </summary>
/// <remarks>
/// Two things were wrong here. We wrote a second bc where the client reads duelType, so the byte it
/// took as the duel type was the low byte of an object id; and nothing ever set a duel type in the
/// first place. The handler at RVA 0x106710 opens with
///
///     call 0x7709D0        ; unit = GetUnitByObjId(bc)
///     test rax, rax / je   ; unknown object -> do nothing
///     test ebx, ebx / je   ; duelType == 0   -> do nothing
///     cmp  ebx, 1          ; "start"
///     cmp  ebx, 2          ; "start_party_duel"
///
/// so a zero duel type makes the client discard the packet in silence - no countdown, no start cue.
/// The bc is the unit the start cue attaches to, and this packet therefore goes to each side
/// separately carrying the OTHER player's object id, the same way SCDuelEnded is written from the
/// recipient's point of view ("opponentUnitIds").
/// </remarks>
public class SCDuelStartedPacket(uint opponentObjId, byte duelType)
    : GamePacket(SCOffsets.SCDuelStartedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(opponentObjId);  // bc - whom this recipient is duelling
        stream.Write(duelType);         // u8 duelType - 1 normal, 2 party duel; 0 is ignored

        return stream;
    }
}
