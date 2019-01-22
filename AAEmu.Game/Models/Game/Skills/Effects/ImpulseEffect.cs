using System;
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

        public override void Apply(Unit caster, SkillAction casterObj, BaseUnit target, SkillAction targetObj, CastAction castObj,
            Skill skill, DateTime time)
        {
            _log.Debug("ImpulseEffect");
        }
    }
}