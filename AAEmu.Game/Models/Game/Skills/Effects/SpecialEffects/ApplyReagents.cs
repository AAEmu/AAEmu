﻿using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;
using NLog;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects
{
    class ApplyReagents : SpecialEffectAction
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        public override void Execute(Unit caster,
            SkillCaster casterObj,
            BaseUnit target,
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
            var skillReagents = SkillManager.Instance.GetSkillReagentsBySkillId(skill.Id);

            if (skillReagents.Count > 0)
            {
                var player = (Character)caster;
                foreach (var reagent in skillReagents)
                {
                    player.Inventory.Bag.ConsumeItem(Items.Actions.ItemTaskType.SkillReagents, reagent.ItemId, reagent.Amount, null);
                }
            }

        }
    }
}
