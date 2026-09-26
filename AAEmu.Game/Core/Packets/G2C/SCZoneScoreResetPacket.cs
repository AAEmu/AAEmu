using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Wire contract: a single <c>uint kind</c>.
/// </summary>
/// <remarks>
/// <para>
/// The packet has no sender in this groundwork slice; it exists so the zone-score reset contract is
/// pinned without starting runtime score state.
/// </para>
/// <para>
/// Field type comes from the raw serializer IR for <c>SCZoneScoreResetPacket</c> (opcode 0x351) in
/// the authoritative packet schema: a single <c>u32 type @ obj_off 16</c>, fixed size 4. This is the
/// clearest of the disputed fields — the raw IR, the derived <c>_packet_structs_*.json</c> and
/// <c>catalog_SC.md</c> all report <c>u32</c>, while base carried <c>int</c> under a comment claiming
/// the type came from the client serializer, a claim that was never itself verified. The type
/// question is open pending review; see the PR thread for the full source comparison.
/// </para>
/// </remarks>
public class SCZoneScoreResetPacket(uint @type) : GamePacket(SCOffsets.SCZoneScoreResetPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(@type);
        return stream;
    }
}
