using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Tasks.Skills;

namespace AAEmu.Game.Models.Game.Skills.Effects;

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

        // The bubble is on its way; the read time only says how long its read window stays open.
        // This used to block the effect thread for the whole read time, which also pushed the cast's
        // EndSkill (TlId release, SCSkillEnded, labor and cooldown bookkeeping) back by that much.
        // The window is scheduled instead, see BubbleReadTimeTask.
        var localizedBubbleText = LocalizationManager.Instance.Get("bubble_effects", "speech", Id, string.Empty);
        var readTime = BubbleReadTimeRules.GetReadTimeMilliseconds(localizedBubbleText);
        TaskManager.Instance.Schedule(new BubbleReadTimeTask(Id, targetObj.ObjId, readTime),
            TimeSpan.FromMilliseconds(readTime));
    }
}
