using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>One serving hero.</summary>
/// <remarks>
/// Only the name travels by reference: the client resolves it from <paramref name="CharId"/> through
/// the name cache. The expedition does not - it is an id the client looks up in its own table.
/// </remarks>
/// <param name="Unk0">Row field 0. X2Hero:GetHeroList never reads it; meaning still unknown.</param>
/// <param name="CharId">Character id. The client resolves name and nameCacheQueryId from this.</param>
/// <param name="FactionId">The hero's NATION, at row +0x10. This is the field the client keys its
/// faction map on - see SCHeroListPacket - so a 0 here leaves GetHeroFactions() empty.</param>
/// <param name="ExpeditionId">
/// The hero's guild, at row +0x14, and the source of the "Expedition:" line under their name.
/// The client compares it against its invalid-id sentinel first and omits the key entirely when they
/// match, so 0 is the correct way to say "no guild" - see SCHeroListPacket.
/// </param>
/// <param name="Ranking">Placing in the leadership ladder that elected them.</param>
/// <param name="Score">Leadership earned in the current period - left figure of the displayed pair.</param>
/// <param name="AccumPoint">Lifetime leadership - right figure of the displayed pair.</param>
/// <param name="Grade">hero_grades row: 1 Eperium, 2 Delphinad, 3 Ayanad, 4 Erenor.</param>
public readonly record struct HeroListEntry(
    int Unk0,
    ulong CharId,
    int FactionId,
    int ExpeditionId,
    int Ranking,
    int Score,
    int AccumPoint,
    byte Grade);

/// <summary>
/// The serving heroes of a nation - the Hero window's "Current Heroes" tab, and what makes
/// X2Hero:IsHero() true.
/// </summary>
/// <remarks>
/// Layout disassembled from the client's reader at .text 0xc62aa0, which reads:
///
///   type   -> +0x10  vtable +0x80  i32   (faction)
///   count  -> +0x14  vtable +0x80  u32
///   rows   -> +0x18, stride 40, each read by the shared row reader at 0xb4aa60
///
/// Row reader field order, with the vtable slot each uses:
///
///   +0x00  type        +0x80  i32
///   +0x08  type        +0x98  u64   charId
///   +0x10  type        +0x80  i32
///   +0x14  type        +0x80  i32
///   +0x18  ranking     +0x80  i32
///   +0x1c  score       +0x80  i32
///   +0x20  accumPoint  +0x80  i32
///   +0x24  type        +0x90  u8
///
/// Slot widths were not assumed. Sampling every reader call in .text 0xc50000-0xc70000 and measuring
/// the gap to the next field gives 0x80 -> 4 bytes (141 samples), 0x98 -> 8 (86), 0x90 -> 1 (45) and
/// 0xf8 -> bool (59, confirmed against the known showUI flag). So the trailing field is a byte, which
/// is the hero grade - hero_grades has exactly four rows and the window renders one badge per hero.
///
/// The 40-byte stride is in-memory alignment (4 bytes of padding after field 0 so charId lands on 8);
/// the wire row is the 33 bytes actually read.
///
/// score and accumPoint confirm the leadership model independently: the client itself names the pair
/// current-period and accumulated, which is what the "Leadership: 6736/41276" line renders.
///
/// Three i32s carry no distinguishing name - the serializer calls them all "type" - but the handler
/// settles which one is the faction. SCHeroList's handler (.text 0x4d4890 -> 0x114430) copies each row
/// verbatim into a map at manager +0xD8 keyed by charId, then calls 0x113ab0, which walks that map and
/// builds the faction map at +0x100 from the field at node +0x30. Node key is 8 bytes at +0x18, so the
/// value starts at +0x20 and node +0x30 is value +0x10 - which is row +0x10.
///
/// So row +0x10 is the NATION. Sending 0 there is why X2Hero:GetHeroFactions() stayed empty however the
/// header was keyed: the rebuild ran, found every hero filed under faction 0, and GetHeroFactions
/// (0x1a72c0) drops entries whose faction fails its lookup.
///
/// Row +0x14 is the EXPEDITION. X2Hero:GetHeroList (.text 0x19eb30) walks the stored rows with a cursor
/// held at row+8 and a stride of 0x28, so its reads translate back by subtracting 8:
///
///   [r13]        -> row +0x08   charId, and from it name and nameCacheQueryId
///   [r13+0x0c]   -> row +0x14   expedition
///   [r13+0x10]   -> row +0x18   ranking
///   [r13+0x14]   -> row +0x1c   score
///   [r13+0x18]   -> row +0x20   leadership (the lifetime total)
///
/// The expedition read is guarded: 0x19ee3d compares it against the invalid-id sentinel and skips the
/// key when they match, and 0x5b5980 then resolves the id to a name in the client's own expedition
/// table. hero_current_status.lua:197 hides the label when the key is absent, so 0 is how a guildless
/// hero is expressed rather than something to avoid sending.
///
/// Field 0 is still unidentified - GetHeroList never reads it.
/// </remarks>
public class SCHeroListPacket(int factionId, IReadOnlyList<HeroListEntry> entries = null)
    : GamePacket(SCOffsets.SCHeroListPacket, 1)
{
    /// <summary>
    /// hero_conditions.hero_candidate_scope is 16, and retail seats six heroes per nation. This is a
    /// sanity bound, not a game rule - the client's array is sized by the packet, unlike the ranking
    /// list's fixed 100-row buffer.
    /// </summary>
    public const int MaxEntries = 64;

    public override PacketStream Write(PacketStream stream)
    {
        var rows = entries ?? [];
        var count = Math.Min(rows.Count, MaxEntries);

        stream.Write(factionId);   // type
        stream.Write((uint)count); // count

        for (var i = 0; i < count; i++)
        {
            var e = rows[i];
            stream.Write(e.Unk0);
            stream.Write(e.CharId);
            stream.Write(e.FactionId);
            stream.Write(e.ExpeditionId);
            stream.Write(e.Ranking);
            stream.Write(e.Score);
            stream.Write(e.AccumPoint);
            stream.Write(e.Grade);
        }

        return stream;
    }
}
