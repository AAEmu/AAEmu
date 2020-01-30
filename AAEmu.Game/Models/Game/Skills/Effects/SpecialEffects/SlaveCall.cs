using System;
using System.Collections.Generic;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Units;
using NLog;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects
{
    public class SlaveCall : ISpecialEffect
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        public void Execute(Unit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
            CastAction castObj, Skill skill, SkillObject skillObject, DateTime time, int Value1, int Value2, int Value3,
            int Value4)
        {
            var owner = (Character)caster;
            var skillData = (SkillItem)casterObj;

            _log.Warn("SlaveCall");

            SlaveManager.Instance.Create(owner, skillData);
        }
    }
}
