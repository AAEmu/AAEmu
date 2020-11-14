using System;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Templates
{
    public class SkillControllerTemplate : EffectTemplate
    {
        public uint KindId { get; set; }
        public int[] Value { get; set; }
        public byte ActiveWeaponId { get; set; }
        // TODO 1.2 // public uint EndSkillId { get; set; }
        public override bool OnActionTime { get; }

        public SkillControllerTemplate()
        {
            Value = new int[15];
        }

        public override void Apply(Unit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
            CastAction castObj,
            EffectSource source, SkillObject skillObject, DateTime time, CompressedGamePackets packetBuilder = null)
        {
            _log.Debug("SkillControllerTemplate");
        }
    }
}
