using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using System;
using AAEmu.Game.Core.Packets;

namespace AAEmu.Game.Models.Game.Skills.Effects
{
    public class ResetAoeDiminishingEffect : EffectTemplate
    {
        public override bool OnActionTime => false;

        public override void Apply(Unit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
            CastAction castObj,
            EffectSource source, SkillObject skillObject, DateTime time, CompressedGamePackets packetBuilder = null)
        {
            _log.Debug("ReportCrimeEffect");
        }
    }
}
