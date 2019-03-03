using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Mate;
using AAEmu.Game.Models.Game.Units;
using NLog;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects
{
    public class PetCall : ISpecialEffect
    {
        protected static Logger _log = LogManager.GetCurrentClassLogger();

        public void Execute(Unit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
            CastAction castObj, Skill skill, SkillObject skillObject, DateTime time, int Value1, int Value2, int Value3,
            int Value4)
        {
            var owner = (Character)caster;
            var skillData = (SkillItem)casterObj;

            switch (Value1)
            {
                case 4944: // TODO - maybe not hardcoded
                    owner.Mates.SpawnMount(skillData);
                    break;
            }
        }
    }
}
