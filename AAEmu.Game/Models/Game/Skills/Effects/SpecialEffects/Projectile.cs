using System;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects
{
    public class Projectile : SpecialEffectAction
    {
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
            // TODO ...
            if (caster is Character) { _log.Debug("Special effects: Projectile value1 {0}, value2 {1}, value3 {2}, value4 {3}", value1, value2, value3, value4); }

            // TODO added for quest Id=2349
            // find the item that was used for Buff and check it in the quests
            if (caster is not Character character) { return; }
            if (casterObj is not SkillItem skillItem) { return; }
            var item = character.Inventory.GetItemById(skillItem.ItemId);
            if (item is {Count: > 0})
            {
                character.Quests.OnItemUse(item);
            }
        }
    }
}
