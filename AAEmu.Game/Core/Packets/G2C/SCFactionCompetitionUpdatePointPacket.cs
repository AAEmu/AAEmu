using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Wire contract: <c>ushort kind</c>, <c>uint kindId</c>, <c>signed int pointDelta</c>.
/// </summary>
/// <remarks>
/// <para>
/// The packet has no sender in this groundwork slice; it exists so the faction-scoring contract is
/// pinned without starting runtime score state.
/// </para>
/// <para>
/// Field types come from the raw serializer IR for
/// <c>SCFactionCompetitionUpdatePointPacket</c> (opcode 0x338) in the authoritative packet schema:
/// <c>u16 type @ obj_off 16</c>, <c>u32 type @ obj_off 20</c> and
/// <c>s32 nUpdatePoint @ obj_off 24, signed</c>, for a fixed size of 10 bytes. The first two fields
/// are agreed by the raw IR, the derived <c>_packet_structs_*.json</c> and <c>catalog_SC.md</c>;
/// only <c>nUpdatePoint</c> is contested, the raw IR saying <c>s32</c> where the other two say
/// <c>u32</c>. Base carried <c>(short, int, uint)</c> under a comment claiming the types came from
/// the client serializer — a claim that was never itself verified against the raw IR. The type
/// question is open pending review; see the PR thread for the full source comparison.
/// </para>
/// </remarks>
public class SCFactionCompetitionUpdatePointPacket(ushort @type, uint @type2, int nUpdatePoint) : GamePacket(SCOffsets.SCFactionCompetitionUpdatePointPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(@type);
        stream.Write(@type2);
        stream.Write(nUpdatePoint);
        return stream;
    }
}
