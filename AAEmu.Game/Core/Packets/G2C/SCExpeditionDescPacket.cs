using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Expeditions;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// The guild info panel's descriptor - level/exp/notice and the rest of X2::ExpeditionDesc. Opcode 0x4B and the
/// full field layout were recovered 2026-08-13 via Ghidra RTTI/constructor analysis of x2game-dev.dll (see the
/// aaemu-fixes-applied memory for the recovery writeup), cross-confirmed against a community-supplied opcode
/// dump. No client-side request opcode exists for this data (CTQueryExpeditionInfoPacket turned out to be an
/// unrelated internal client cache mechanism, not a network packet) - this must be server-pushed, matching the
/// existing SendExpeditionInfo pattern (login, create, invite-accept).
/// </summary>
public sealed class SCExpeditionDescPacket(Expedition expedition) : GamePacket(SCOffsets.SCExpeditionDescPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        // FactionDesc base portion (id, motherId, name, owner, ..., integrationFaction) - byte-identical field
        // order/widths to what Ghidra recovered, and this Write() already existed and is shared with
        // SCExpeditionListPacket/SCFactionCreatedPacket - independent confirmation it was already correct.
        stream.Write(expedition);

        // ExpeditionDesc-specific tail, network order per the Ghidra recovery. Level/Exp/contributionPoint
        // are the fields with real backing data right now (Expedition.Level/Exp, TotalContributionPoint -
        // the live sum of every member's own ContributionPoint, not a separately persisted bank).
        // Everything else below belongs to subsystems that don't exist in this codebase yet (guild war
        // win/lose/draw scoreboard, war deposit/protection, a rename cooldown) - written as zero/default
        // rather than guessed, and two fields (rows 1/2 in the recovery, both keyed "type" on the wire) had
        // no confirmed meaning at all, so those two are omitted here already (they're Id/MotherId, already
        // covered by the base Write() above - matches the recovery's own note that the generic "type" key
        // is a serializer artifact for enum-typed fields, not a real distinct field).
        stream.Write((int)expedition.Level);
        stream.Write((int)expedition.Exp);
        // 2026-08-27: REVERTED a field-order "fix" attempted earlier tonight (had swapped these 4 fields to
        // dailyExp/lastExpUpdateTime/protectDate/warDeposit based on a decompile read that was supposed to be
        // byte-count-neutral). User reported the guild dominion protection-time display went haywire (absurd
        // hour counts) immediately after that change went live - so despite the careful-looking byte-count
        // math, something about that reorder (or the underlying width assumptions for these slots) was wrong
        // in practice. Reverted to this original order rather than guess again. Do not re-attempt reordering
        // these 4 fields without real (live-debugged) evidence - two speculative "fixes" to this packet
        // tonight have each made a different part of the guild UI worse, not better.
        // 2026-09-02: value-only change (same slot/order the note above warns not to reorder) - now that
        // Expedition tracks real Guild War state, show the actual deadline instead of always "now". While a
        // war is active this is when it ends; once it ends this is the post-war re-declaration protection
        // deadline; with no war ever fought it's still just "now" as before.
        stream.Write(expedition.WarEndsAt ?? expedition.WarProtectedUntil ?? DateTime.UtcNow); // protectDate
        stream.Write(0);                   // warDeposit - not tracked
        stream.Write(0);                   // dailyExp - not tracked separately from Exp yet
        stream.Write(DateTime.UtcNow);     // lastExpUpdateTime
        // 2026-08-27: was hardcoded 0 ("not tracked") - now backed by Expedition.Interest, settable via
        // CSExpeditionInterestUpatePacket (see ExpeditionManager.SetInterest), which was found fully parsed
        // but never registered during the full-codebase unregistered-packet audit.
        stream.Write(expedition.Interest);
        stream.Write(Truncate(expedition.Notice, 800));
        stream.Write(0);                   // win - GuildWar not built yet
        stream.Write(0);                   // lose
        stream.Write(0);                   // draw
        // Row 25 was previously written as 0 ("unconfirmed field"). Ghidra-confirmed 2026-08-21: this is a
        // client-cached gate on the guild-level-up button - FUN_396b81c0 (the native handler behind
        // X2Faction:SetExpeditionLevelUp) only actually sends CSExpeditionLevelUpPacket when
        // *(int*)(cache+0x370) == 0xff (an OR-branch alongside an unrelated account/session flag read via a
        // separate global). cache+0x370 sits right after this SAME struct's cache+0x36c field. 2026-08-27
        // CORRECTION: cache+0x36c is NOT "the cached Level field" as previously assumed here - independently
        // re-derived by decompiling the getter function it goes through (FUN_396b6040), which is ALSO what
        // backs the native X2Faction:GetMyExpeditionId() Lua binding. cache+0x36c is the client's cached
        // "which guild am I in" id, populated by some OTHER mechanism entirely (not by this packet's own
        // fields) - most likely SCUnitExpeditionChangedPacket sent to oneself at login, unconfirmed which
        // exact packet/handler sets it since the native vtable dispatch for that isn't visible in this text
        // decompile dump. This also means X2Faction:IsExpedInfoLoaded() (FUN_396b6b90, checks cache+0x0 ==
        // cache+0x36c) is a "does the cached desc's own guild id match my cached guild id" freshness check,
        // not a Level check - see aaemu-guild-systems-gaps memory for the fuller trail (member list stuck
        // loading + invite permanently disabled both gate on this same check). Writing 0 here silently
        // blocked every level-up attempt client-side before any packet was ever sent - no error, no
        // server-side symptom at all. 0xff appears to mean "no restriction" (a war-protection cooldown/lock
        // code sentinel is the most likely real semantic, matching this field's neighbors in the recovery,
        // but unconfirmed beyond satisfying the gate).
        stream.Write(0xff);
        stream.Write(0L);                  // unconfirmed field (recovery row 26, generic "type" key)
        stream.Write((long)expedition.TotalContributionPoint); // contributionPoint (guild-level total) - sum of all members' own ContributionPoint
        stream.Write(0);                   // dailyContributionPoint
        stream.Write(DateTime.UtcNow);     // lastContributionPointAdded
        stream.Write(DateTime.UtcNow);     // lastAssignmentUpdateTime
        return stream;
    }

    private static string Truncate(string value, int maxLength) =>
        string.IsNullOrEmpty(value) ? string.Empty : value[..Math.Min(value.Length, maxLength)];
}
