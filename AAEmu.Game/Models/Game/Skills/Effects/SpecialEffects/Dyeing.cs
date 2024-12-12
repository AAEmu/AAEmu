using System;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class Dyeing : SpecialEffectAction
{
    public override void Execute(BaseUnit caster,
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
        if (caster is Character) { Logger.Debug("Special effects: Dyeing value1 {0}, value2 {1}, value3 {2}, value4 {3}", value1, value2, value3, value4); }

        // TODO: Should check whether the item being dyed is actually dyeable (listed in dyeable_items table).

        var owner = (Character)caster;
        var skillItem = casterObj as SkillItem;
        var skillTargetItem = (SkillCastItemTarget)targetObj;
        var targetItem = owner.Inventory.GetItemById(skillTargetItem.Id);
        var equipItem = (EquipItem)targetItem;

        // Check the item used is in the Dye (33) category.
        // Could alternatively look up the item template ID in the item_dyeings table to verify it can be used as a dye.
        var template = ItemManager.Instance.GetTemplate(skillItem.ItemTemplateId);
        if (template.CategoryId != 33)
        {
            Logger.Warn($"{owner.Name} tried to dye with a non-dye item - item id {skillItem.ItemId} and template id {skillItem.ItemTemplateId}");
            return;
        }

        equipItem.DyeItemId = skillItem.ItemTemplateId;

        owner.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.Dyeing, [new ItemUpdate(equipItem)], []));
    }
}
