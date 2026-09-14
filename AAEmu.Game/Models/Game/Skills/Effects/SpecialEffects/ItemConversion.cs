using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class ItemConversion : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.ItemConversion;
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
        if (caster is Character) { Logger.Debug("Special effects: ItemConversion value1 {0}, value2 {1}, value3 {2}, value4 {3}", value1, value2, value3, value4); }

        if (caster is not Character character)
        {
            skill.Cancelled = true;
            return;
        }

        if (targetObj is not SkillCastItemTarget itemTarget)
        {
            skill.Cancelled = true;
            return;
        }

        var targetItem = character.Inventory.Bag.GetItemByItemId(itemTarget.Id);
        if (targetItem == null)
        {
            skill.Cancelled = true;
            return;
        }

        if (!targetItem.Template.Disenchantable)
        {
            skill.Cancelled = true;
            return;
        }

        var id = targetItem.TemplateId;
        var reagent = ItemConversionGameData.Instance.GetReagentForItem(
            targetItem.Grade,
            targetItem.Template.ImplId,
            id,
            targetItem.Template.Level,
            targetItem.Template.CategoryId);
        if (reagent == null)
        {
            Logger.Error($"Couldn't find Reagent for item {id}");
            skill.Cancelled = true;
            return;
        }

        // value1 names the item_conv_sets family this effect performs. Refuse a cast that asks for another
        // family: an awakening effect must not run the disenchant chain off the same reagent pack.
        //
        // Only enforced when the item's own family is known. 48 of the 5618 reagent packs the content
        // references reach a conversion whose item_convs.item_conv_set_id is NULL (the origin-land armour
        // sockets, the raid-to-Ipnir exchange, the discontinued mate armours), covering 574 items; rejecting
        // on unknown data would break every one of them.
        if (value1 > 0 && reagent.ConversionSet != 0 && !ItemConversionGameData.Instance.IsValidConversionSet(value1, reagent))
        {
            Logger.Warn(
                "ItemConversion: skill {0} asked for conversion set {1} but item {2} belongs to set {3}",
                skill.Template?.Id, value1, id, reagent.ConversionSet);
            skill.Cancelled = true;
            return;
        }

        if (!ItemConversionGameData.Instance.TryRollProduct(reagent, out var roll))
        {
            Logger.Error($"Couldn't find Product from Reagent for item {id}");
            skill.Cancelled = true;
            return;
        }

        if (!roll.ChanceFailed && roll.Count > 0)
        {
            var grade = roll.Product.GradeId > 0 ? roll.Product.GradeId : -1;
            if (!character.Inventory.Bag.AcquireDefaultItem(ItemTaskType.Conversion, roll.Product.OutputItemId, roll.Count, grade))
            {
                skill.Cancelled = true;
                character.SendErrorMessage(ErrorMessageType.BagFull);
                return;
            }
        }
        else
        {
            Logger.Debug("ItemConversion: item {0} (pack {1}) rolled no product", id, reagent.ReagentPackId);
        }

        // consumes target item from stack or if there is only 1, destroy item
        targetItem._holdingContainer.ConsumeItem(ItemTaskType.Conversion, targetItem.TemplateId, 1, targetItem);
    }
}
