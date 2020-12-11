using System;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects
{
    public class GiveBmMileage : SpecialEffectAction
    {
        public override void Execute(Unit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj, CastAction castObj,
            Skill skill, SkillObject skillObject, DateTime time, int value1, int value2, int value3, int value4)
        {
            if (!(caster is Character character))
                return;

            character.BmPoint += value1;
            character.SendPacket(new SCMileageChangedPacket(character.ObjId, (int) character.BmPoint));
        }
    }
}
