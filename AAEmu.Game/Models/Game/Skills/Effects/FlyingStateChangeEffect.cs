using System;

using AAEmu.Game.Core.Packets;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects;

public class FlyingStateChangeEffect : EffectTemplate
{
    public bool FlyingState { get; set; }

    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        Logger.Info($"FlyingStateChangeEffect: npc={caster.TemplateId}:{caster.ObjId}, FlyingState={FlyingState}");

        var packet = new SCUnitFlyingStateChangedPacket(caster.ObjId, FlyingState);
        if (packetBuilder != null)
            packetBuilder.AddPacket(packet);
        else
            caster.BroadcastPacket(packet, true);

    }
}
