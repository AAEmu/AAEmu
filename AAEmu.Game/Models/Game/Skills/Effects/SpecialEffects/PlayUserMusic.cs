using System;

using AAEmu.Game.Models.Game.Units;

using NLog;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects
{
    public class PlayUserMusic : SpecialEffectAction
    {
        protected override SpecialType SpecialEffectActionType => SpecialType.PlayUserMusic;
        
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
            _log.Warn("SpecialEffectAction - PlayUserMusic - value1 {0}, value2 {1}, value3 {2}, value4 {3}", value1, value2, value3, value4);
            // TODO: make sure the proper instrument buff gets applied
            // The related tags seems to be "Play Song" (1155) and "Music Play Animation" (1202)
            
            //target.Buffs.AddBuff(6176, caster); // Flute Play 
            target.Buffs.AddBuff(6177, caster); // Lute Play 
        }
    }
}
