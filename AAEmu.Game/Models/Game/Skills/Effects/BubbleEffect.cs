using System;
using System.Threading;
using System.Threading.Tasks;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

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

        // Estimate the read time
        // https://iovs.arvojournals.org/article.aspx?articleid=2166061
        // According to a study conducted in 2012, the average reading speed of an adult for text in English is:
        // 228±30 words, 313±38 syllables, and 987±118 characters per minute.
        // We will use a low-end of 900 character / minute
        var localizedBubbleText = LocalizationManager.Instance.Get("bubble_effects", "speech", Id, string.Empty);
        var readTime = localizedBubbleText == string.Empty ? 2500 : (int)Math.Round(localizedBubbleText.Length * 0.015);
        readTime = Math.Max(readTime, 1250); // 1.25 seconds minimum popup time
        Thread.Sleep(readTime);
    }
}
