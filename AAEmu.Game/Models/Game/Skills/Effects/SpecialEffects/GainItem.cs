using System;

using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Units;
namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects
{
    public class GainItem : SpecialEffectAction
    {
        public override void Execute(Unit caster,
            SkillCaster casterObj,
            BaseUnit target,
            SkillCastTarget targetObj,
            CastAction castObj,
            Skill skill,
            SkillObject skillObject,
            DateTime time,
            int value1, // Item TemplateId
            int value2, // Item Count
            int value3,
            int value4)
        {
            // TODO ...
            if (caster is Character) { _log.Debug("Special effects: GainItem value1 {0}, value2 {1}, value3 {2}, value4 {3}", value1, value2, value3, value4); }
            
            if (caster is Character character)
            {
                character.Inventory.Bag.AcquireDefaultItem(ItemTaskType.Loot, (uint)value1, 1, 0);
            }
        }
    }
}
