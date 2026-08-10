using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>Which hero schedule event is running, and for which season.</summary>
/// <param name="ScheduleEvent">
/// enum_hero_schedule_events row, the same values hero/common.lua names:
/// 1 leadership_ranking, 2 hero_abstain, 3 hero_voting, 4 hero_period.
/// Must not be 0: the client rejects an entry whose event is zero.
/// </param>
/// <param name="Season">
/// The season this event belongs to - heros.id, which is hero_schedules.hero_id. NOT the faction.
/// The client stores the entry at heroManager + 12*event + 0x70 and resolves it by scanning
/// hero_schedules for the row whose event_id and hero_id both match (.text 0x98be90), so a value that
/// is not a season id finds nothing and every feature gated on that event stays off.
/// </param>
/// <param name="State">
/// Whether that event is running. Anything but 2 counts as live (.text 0x108046), so 2 is the "over"
/// state; 1 is what an active window sends.
/// </param>
public readonly record struct HeroEventStateEntry(byte ScheduleEvent, int Season, byte State);

/// <summary>
/// Publishes the hero schedule state per faction.
/// </summary>
/// <remarks>
/// Layout from the client's reader at .text 0xc541d0 and its per-entry reader at 0xd27600:
///
///   clearAll  vtable +0xf8  bool
///   count     vtable +0x80  u32
///   entries   at +24, in-memory stride 12, each:
///       HeroScheduleEvent  +0x90  u8
///       type               +0x80  i32   (the season - see HeroEventStateEntry.Season)
///       state              +0x90  u8
///
/// The entry is three i32 slots in memory but 6 bytes on the wire - the two u8 reads are widened on
/// store, which is why the stride is 12 and the payload is not.
///
/// This is what turns the hero schedule on client-side. The manager keeps one slot per event kind
/// (5 of them, at +0x70 + 12*event) and every lookup goes through .text 0x108010, which wants the slot
/// filled, its state not 2, and the (event, season) pair to exist in hero_schedules. Peer rating is
/// gated on the leadership_ranking slot resolving: the gate at 0x164f20 reads the schedule row, follows
/// heros.hero_condition_id and compares the player against hero_conditions.
/// </remarks>
public class SCHeroEventStatePacket(bool clearAll, IReadOnlyList<HeroEventStateEntry> entries)
    : GamePacket(SCOffsets.SCHeroEventStatePacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        var rows = entries ?? [];

        stream.Write(clearAll);
        stream.Write((uint)rows.Count);

        foreach (var e in rows)
        {
            stream.Write(e.ScheduleEvent);
            stream.Write(e.Season);
            stream.Write(e.State);
        }

        return stream;
    }
}
