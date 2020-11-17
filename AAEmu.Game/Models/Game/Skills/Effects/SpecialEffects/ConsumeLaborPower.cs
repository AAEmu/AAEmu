using System;
using System.Linq;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;
using NLog;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects
{
    class ConsumeLaborPower : SpecialEffectAction
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
            //TODO: Need to factor skill level into how much lp we subtract.
            if(skill.Template.ConsumeLaborPower > 0)
            {
                var player = (Character)caster;
                player.ChangeLabor((short)-skill.Template.ConsumeLaborPower, skill.Template.ActabilityGroupId);
            }
        }
    }
}
