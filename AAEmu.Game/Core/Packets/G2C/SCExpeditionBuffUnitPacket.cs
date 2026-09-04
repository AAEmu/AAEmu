using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Answers CSExpeditionBuffUnitPacket (a bare unit Bc, sent repeatedly by the client whenever the
/// prestige-shop buff window is open - never previously wired to anything, "nothing acts on it yet").
/// </summary>
/// <remarks>
/// 2026-08-28: wire layout found by decompiling FUN_39c87fd0 (x2game-dev.dll) - the real Unpack for
/// this packet, sitting right next to SCExpeditionBuffsPacket's own Unpack (FUN_39c87f60) in the
/// dump, previously misread as unrelated. Layout: [unitId: optional-presence-wrapped 3-byte Bc @
/// +0x10, via the same slot 0x1a0/0x1a8 pattern already confirmed for SCUnitExpeditionChangedPacket]
/// + ["buffs" vector @ +0x18, byte-identical 12-byte entries to SCExpeditionBuffsPacket's own format].
///
/// 2026-09-02: per-entry field bindings corrected the same way as SCExpeditionBuffsPacket.cs - the
/// real layout (via FUN_39aa9810, see that file's remarks for the full trail) is
/// {buffType, grade, learnedGrade}, not {buffId, unknown, grade}. Same fix applied here for
/// consistency since this packet shares the identical 12-byte struct. Semantics of "why per-unit"
/// still not confirmed - modeled as an echo of the requesting character's own unit id plus the
/// guild's current buff-grade state, since the client only ever sends its own unit's Bc in the
/// request. Untested live.
/// </remarks>
public class SCExpeditionBuffUnitPacket(uint unitId, IReadOnlyDictionary<uint, byte> purchasedGrades)
    : GamePacket(SCOffsets.SCExpeditionBuffUnitPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(unitId);
        stream.Write(purchasedGrades.Count);
        foreach (var (buffId, grade) in purchasedGrades)
        {
            stream.Write(buffId);
            stream.Write((uint)grade); // +0x4 = "grade" - was hardcoded 0
            stream.Write((uint)grade); // +0x8 = "learnedGrade" - was the real grade, in the wrong slot
        }
        return stream;
    }
}
