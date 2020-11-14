using System;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects
{
    public class ImpulseEffect : EffectTemplate
    {
        public float VelImpulseX { get; set; }
        public float VelImpulseY { get; set; }
        public float VelImpulseZ { get; set; }
        public float AngvelImpulseX { get; set; }
        public float AngvelImpulseY { get; set; }
        public float AngvelImpulseZ { get; set; }
        public float ImpulseX { get; set; }
        public float ImpulseY { get; set; }
        public float ImpulseZ { get; set; }
        public float AngImpulseX { get; set; }
        public float AngImpulseY { get; set; }
        public float AngImpulseZ { get; set; }

        public override bool OnActionTime => false;

        public override void Apply(Unit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
            CastAction castObj,
            EffectSource source, SkillObject skillObject, DateTime time, CompressedGamePackets packetBuilder = null)
        {
            _log.Debug("ImpulseEffect");
        }
    }
}
