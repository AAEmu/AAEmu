using System;
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
    }
}
