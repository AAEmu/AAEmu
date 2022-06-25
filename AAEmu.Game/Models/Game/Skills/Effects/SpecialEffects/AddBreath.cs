﻿using System;

using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

using NLog;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects
{
    public class AddBreath : SpecialEffectAction
    {
        protected override SpecialType SpecialEffectActionType => SpecialType.AddBreath;

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
            if (caster is Character character && character.IsUnderWater)
            {
                character.Breath = (uint)Math.Min(character.LungCapacity, character.Breath + value1);
            }
        }
    }
}
