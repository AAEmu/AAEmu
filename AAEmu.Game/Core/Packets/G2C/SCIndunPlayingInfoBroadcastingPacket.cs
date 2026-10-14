using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// The instance HUD readouts that are not a plain counter: one row per npc (its buff's stack count or
/// remaining time) and the copy's gain rules — the "time until dawn" readout of zone group 130 among them.
/// </summary>
/// <remarks>
/// <para>
/// The packet carries the copy's zone instance id and then a nested buffer (<see cref="NestedBlobWire"/>)
/// that the client parses in a second pass. The client only accepts the readout for the copy it is playing,
/// so <paramref name="zoneInstanceId"/> must be the same id the copy was handed over with. Inside the buffer
/// are <b>three</b> counted arrays, in this order:
/// </para>
/// <code>
/// ZoneInstanceId zi (u32 zone key, u32 instance id)
/// nested buffer:
///   u32  npcInfoCount      { u32 buffId; u32 npcId; u32 broadcastingType; u32 value } x count
///   u32  gainRuleIdCount   { u32 gainRuleId } x count
///   u32  gainRuleInfoCount { u64 gainRuleId } x count
/// </code>
/// <para>
/// The two id lists are the same reading in two widths: the client resolves both through one template
/// lookup and merges them into the single <c>gainRuleInfo</c> readout. An empty list is still written as a
/// zero count, or the last count is read out of the wrong bytes and the whole packet is discarded.
/// </para>
/// <para>
/// Row meaning: <c>buffId</c>/<c>npcId</c> come from <c>indun_event_npc_info_broadcastings</c>, and
/// <c>broadcastingType</c> is that row's <c>npc_info_broadcasting_id</c> — 1 a buff stack count, 2 a buff's
/// remaining time in milliseconds — which is what <c>value</c> holds. The client keys its readout by
/// <c>buffId</c> (it names the row after the buff and keeps the latest value per buff), so the buff id leads
/// the row and two readouts must not share one buff. Every row is produced from the loaded tables, so an
/// instance that uses them gets the HUD without per-instance code.
/// </para>
/// </remarks>
public class SCIndunPlayingInfoBroadcastingPacket(
    ZoneInstanceId zoneInstanceId,
    IReadOnlyList<IndunPlayingInfoNpc> npcInfo,
    IReadOnlyList<uint> gainRuleIds,
    IReadOnlyList<ulong> gainRuleInfo)
    : GamePacket(SCOffsets.SCIndunPlayingInfoBroadcastingPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(zoneInstanceId ?? new ZoneInstanceId(0, 0));

        var payload = new PacketStream();
        payload.Write((uint)(npcInfo?.Count ?? 0));
        if (npcInfo != null)
        {
            foreach (var row in npcInfo)
            {
                payload.Write(row.BuffId);
                payload.Write(row.NpcId);
                payload.Write(row.BroadcastingType);
                payload.Write(row.Value);
            }
        }

        payload.Write((uint)(gainRuleIds?.Count ?? 0));
        if (gainRuleIds != null)
        {
            foreach (var gainRuleId in gainRuleIds)
                payload.Write(gainRuleId);
        }

        payload.Write((uint)(gainRuleInfo?.Count ?? 0));
        if (gainRuleInfo != null)
        {
            foreach (var gainRuleId in gainRuleInfo)
                payload.Write(gainRuleId);
        }

        NestedBlobWire.Write(stream, payload);
        return stream;
    }
}

/// <summary>
/// One <c>npcInfo</c> row of <see cref="SCIndunPlayingInfoBroadcastingPacket"/>. <see cref="Limit"/> (a timer's
/// whole duration) is not on the wire — the server uses it to tell a restarted timer from one counting down.
/// </summary>
public readonly record struct IndunPlayingInfoNpc(uint NpcId, uint BuffId, uint BroadcastingType, uint Value, uint Limit);
