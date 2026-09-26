using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Wire contract: <c>uint kind</c> followed by a <c>signed int scoreDelta</c>.
/// </summary>
/// <remarks>
/// <para>
/// The packet has no sender in this groundwork slice; it exists so the zone-score contract is
/// pinned without starting runtime score state.
/// </para>
/// <para>
/// Field types come from the raw serializer IR for <c>SCZoneScoreUpdatePacket</c> (opcode 0x350) in
/// the authoritative packet schema: <c>u32 type @ obj_off 16</c> and <c>s32 diff @ obj_off 20,
/// signed</c>, for a fixed size of 8 bytes. The derived <c>_packet_structs_*.json</c> reports both
/// as unsigned; that view loses signedness and must not be used to settle a type. Base carried
/// <c>(int, uint)</c> under a comment claiming the types came from the client serializer — a claim
/// that was never itself verified against the raw IR, which is how the two disagreed. The type
/// question is open pending review; see the PR thread for the full source comparison.
/// </para>
/// </remarks>
public class SCZoneScoreUpdatePacket(uint @type, int diff) : GamePacket(SCOffsets.SCZoneScoreUpdatePacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(@type);
        stream.Write(diff);
        return stream;
    }
}
