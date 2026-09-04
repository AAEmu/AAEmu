using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Full sync of a guild's currently-purchased prestige-shop buff grades - answers CSExpeditionBuffPacket's
/// "view" request and should also be pushed on Expedition join/login (see ExpeditionManager.SendExpeditionBuffs).
/// </summary>
/// <remarks>
/// Wire layout: [expeditionId:4 @+0x10][8-byte field @+0x38]["buffs" vector @+0x18, 12-byte
/// int32 triples, int32 count]. The header/count/8-byte-freshness-timestamp part of this was
/// GROUND-TRUTH-CONFIRMED 2026-08-28 via a real Frida live capture (hooked Unpack, FUN_39c87f60,
/// and read the destination struct after every real call) - that capture only verified the raw
/// bytes arrive intact, not what each per-entry slot MEANS to the client, which is a separate
/// question this comment used to answer wrong.
///
/// 2026-09-02: found the real per-entry field bindings via `FUN_39aa9810` (the concrete converter
/// `FUN_39ace420`'s per-element loop calls for this exact 12-byte struct, reached from this
/// packet's own Unpack, FUN_39c87f60 -&gt; FUN_39ace420("buffs", ...) -&gt; FUN_39aae740 -&gt;
/// FUN_39aa9810 - a hardcoded, non-generic call chain, not a templated one, so this binding is
/// specific to this struct):
/// <code>
/// (**(code **)(*param_2 + 0x80))(param_2, &amp;DAT_3a24f540, param_1, 0);      // +0x0 = buffType
/// (**(code **)(*param_2 + 0xa0))(param_2, "grade", param_1 + 4, 0);        // +0x4 = grade
/// (**(code **)(*param_2 + 0xa0))(param_2, "learnedGrade", param_1 + 8, 0); // +0x8 = learnedGrade
/// </code>
/// The real layout is {buffType, grade, learnedGrade} - NOT {buffId, unknown, grade} as this
/// packet previously assumed. That earlier assumption always wrote a hardcoded 0 into the +0x4
/// slot (thinking it was an unused/unknown field) and the real purchased grade into +0x8 - so the
/// client's `RefreshLeftList` (which displays `v.grade` directly as the buff-shop's "x/y" numerator)
/// always read 0, matching the reported "0/x" symptom exactly, independent of the notify-timestamp
/// bug fixed earlier the same day (that fix was real and necessary, just not sufficient - the
/// cache was updating correctly, just with the grade value in the wrong slot).
///
/// `learnedGrade`'s exact semantics are unconfirmed (the client also uses it for an
/// "EXPEDITION_HOUSE_NOT_EXIST" comparison against `grade` elsewhere) - sending the same value as
/// `grade` here is a deliberate, evidence-based safe default (avoids inventing a spurious
/// housing-required error for grades already legitimately owned) until a live capture nails down
/// whether it should ever legitimately differ.
/// </remarks>
public class SCExpeditionBuffsPacket(uint expeditionId, IReadOnlyDictionary<uint, byte> purchasedGrades)
    : GamePacket(SCOffsets.SCExpeditionBuffsPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(expeditionId);
        stream.Write(DateTime.UtcNow.Ticks); // was a hardcoded 0L - see remarks, this was a real bug too
        stream.Write(purchasedGrades.Count);
        foreach (var (buffId, grade) in purchasedGrades)
        {
            stream.Write(buffId);
            stream.Write((uint)grade); // +0x4 = "grade" - was hardcoded 0, see remarks
            stream.Write((uint)grade); // +0x8 = "learnedGrade" - was the real grade, in the wrong slot
        }
        return stream;
    }
}
