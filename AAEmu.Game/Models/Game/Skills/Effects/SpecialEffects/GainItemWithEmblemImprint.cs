using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Stream;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects
{
    public class GainItemWithEmblemImprint : SpecialEffectAction
    {
        protected override SpecialType SpecialEffectActionType => SpecialType.GainItemWithEmblemImprint;
        
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
            if (caster is Character) { _log.Debug("Special effects: GainItemWithEmblemImprint value1 {0}, value2 {1}, value3 {2}, value4 {3}", value1, value2, value3, value4); }

            var sourceItem = ItemManager.Instance.GetItemByItemId(((SkillItem)casterObj).ItemId);
            if ((sourceItem != null) && (target is Character player))
            {
                UccManager.Instance.CreateStamp(player, sourceItem);
            }
        }
    }
}
