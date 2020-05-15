using System;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects
{
    public class Blink : SpecialEffectAction
    {
        public override void Execute(Unit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
            CastAction castObj,
            Skill skill, SkillObject skillObject, DateTime time, int value1, int value2, int value3, int value4)
        {
            if (caster is Character character) {
                var (endX, endY) = MathUtil.AddDistanceToFront(value1, character.Position.X, character.Position.Y, (sbyte)value2);
                var endZ = character.Position.Z;
                character.SendPacket(new SCBlinkUnitPacket(caster.ObjId, value1, value2, endX, endY, endZ));
            }
        }
    }
}
