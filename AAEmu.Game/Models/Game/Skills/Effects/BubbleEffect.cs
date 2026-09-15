using AAEmu.Game.Core.Packets;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects;

/// <summary>
/// Shows a chat bubble above the target and returns. Nothing waits for the line to be read.
/// </summary>
/// <remarks>
/// <para>
/// The effect used to end with a blocking sleep of <c>0.015 * characters</c> clamped to 1 250 ms, taken
/// from a citation of 900 characters per minute. 900 per minute is 66.7 ms per character, not 0.015, so
/// the term never rose above the floor: every shipped line resolved to a flat 1 250 ms (the longest
/// localized line is 264 characters and scored 4 ms). Deriving it properly would hold that line for
/// 17.6 s, which no bubble should, and there is no column or client field for a bubble duration —
/// <see cref="SCChatBubblePacket"/> carries the id alone and the client retires the bubble on its own.
/// There is no read window to keep, so none is scheduled either.
/// </para>
/// <para>
/// What the sleep also did was pace the cast: it held the effect pipeline and pushed the cast's
/// <c>EndSkill</c> (TlId release, <c>SCSkillEnded</c>, labor and cooldown bookkeeping) back by the same
/// 1 250 ms. Both are gone by design. 40 skills apply two bubbles that both actually apply (weighted
/// alternatives excluded) and 553 skills have an effect ordered after a bubble, so those now run back to
/// back instead of one line at a time; restoring that pacing belongs to the effect pipeline, which would
/// have to await a per-effect hold rather than block a thread inside an effect.
/// </para>
/// </remarks>
public class BubbleEffect : EffectTemplate
{
    public uint KindId { get; set; }

    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        // var sampleText = LocalizationManager.Instance.Get("bubble_effects", "speech", Id, "");
        Logger.Trace($"BubbleEffect, Id {Id}, KindId {KindId}, ObjId {targetObj.ObjId}"); //, Text {sampleText}");
        // TODO: Verify if this can be a normal Broadcast, or if it should only go towards the caster and/or target
        target?.BroadcastPacket(new SCChatBubblePacket(targetObj.ObjId, (byte)KindId, 2, Id, ""), true);
    }
}
