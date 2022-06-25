using System;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects
{
    public class GiveLivingPoint : SpecialEffectAction
    {
        public override void Execute(IUnit caster,
            SkillCaster casterObj,
            IBaseUnit target,
            SkillCastTarget targetObj,
            CastAction castObj,
            Skill skill,
            SkillObject skillObject,
            DateTime time,
            int value1,
            int value2,
            int value3,
            int value4)
        {
            if (!(caster is Character character))
                return;

            var points = (int)Math.Round(AppConfiguration.Instance.World.VocationRate * value1);
            character.ChangeGamePoints(GamePointKind.Vocation,points);
        }
    }
}
