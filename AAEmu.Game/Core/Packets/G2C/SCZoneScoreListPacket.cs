using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>One entry in the zone-score list: a kind id and its signed score.</summary>
public readonly record struct ZoneScoreListEntry(uint Type, int Score);

/// <summary>
/// Wire contract: a signed entry count followed by <c>{ uint kind, signed int score }</c> entries.
/// </summary>
/// <remarks>
/// <para>
/// This slice only defines the packet shape; it does not read or mutate score state. A future
/// sender must establish any client-side list bound from live evidence before adding one here.
/// </para>
/// <para>
/// The count comes from the raw serializer IR for <c>SCZoneScoreListPacket</c> (opcode 0x34F) in the
/// authoritative packet schema: <c>s32 count @ obj_off 16, signed</c>, followed by a guarded loop of
/// elements. The raw IR clamps the count three times, with a limit of 20.
/// </para>
/// <para>
/// <b>The element layout is the weakest part of this contract.</b> The derived
/// <c>_packet_structs_*.json</c> and <c>catalog_SC.md</c> both report <c>u32 count</c> and then list
/// the element fields at <c>obj_off 4</c> — both of them, which is the derived flattener hoisting a
/// guarded field to the top level and zeroing its offset. Those two views therefore cannot settle
/// either the count's signedness or the element's field types, and no element type has been
/// recovered. <c>{ uint kind, signed int score }</c> is this PR's reading of the element and is
/// unverified; treat it as provisional and re-derive it from the raw serializer before any sender
/// relies on it. See the PR thread for the full source comparison.
/// </para>
/// </remarks>
public class SCZoneScoreListPacket(IReadOnlyList<ZoneScoreListEntry> entries)
    : GamePacket(SCOffsets.SCZoneScoreListPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        var source = entries ?? [];
        stream.Write(source.Count);
        for (var i = 0; i < source.Count; i++)
        {
            stream.Write(source[i].Type);
            stream.Write(source[i].Score);
        }
        return stream;
    }
}
