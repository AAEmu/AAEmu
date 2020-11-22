using System;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects
{
    public class ConversionEffect : EffectTemplate
    {
        public uint CategoryId { get; set; }
        public uint SourceCategoryId { get; set; }
        public int SourceValue { get; set; }
        public uint TargetCategoryId { get; set; }
        public int TargetValue { get; set; }

        public override bool OnActionTime => false;

        public override void Apply(Unit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
            CastAction castObj,
            EffectSource source, SkillObject skillObject, DateTime time, CompressedGamePackets packetBuilder = null)
        {
            _log.Debug("ConversionEffect");
        }
    }
}
