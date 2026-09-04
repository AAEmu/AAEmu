using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

/// <summary>
/// Element specialisation - the third sub-menu of the Synthesis tab, 장비 특화하기 in the data.
/// Spends the item's banked synthesis experience to raise its element level.
/// </summary>
/// <remarks>
/// The ladder is <c>item_rnd_attr_category_elements</c>, keyed by the item's synthesis pool: each
/// row names a level and the experience it costs. The pool's per-grade
/// <c>max_element_level</c> is the ceiling.
/// </remarks>
public class ItemElement : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.ItemElement;

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
        if (caster is not Character owner)
        {
            Logger.Error("ItemElement: caster {0} is not a character", caster?.Id);
            return;
        }

        if (targetObj is not SkillCastItemTarget itemTarget)
        {
            Logger.Warn("ItemElement: target {0} is not an item", targetObj);
            return;
        }

        if (owner.Inventory.GetItemById(itemTarget.Id) is not EquipItem equipItem)
        {
            Logger.Warn("ItemElement: item {0} not found or not equipment", itemTarget.Id);
            return;
        }

        var categoryId = (equipItem.Template as EquipItemTemplate)?.RndAttrCategoryId ?? 0;
        var property = categoryId == 0
            ? null
            : ItemEnchantGameData.Instance.GetRndAttrProperty(categoryId, equipItem.Grade);

        var oldLevel = equipItem.ElementLevel;
        var nextLevel = (byte)(oldLevel + 1);

        var step = categoryId == 0 ? null : ItemEnchantGameData.Instance.GetElementStep(categoryId, nextLevel);
        var capped = property != null && nextLevel > property.MaxElementLevel;

        if (step == null || capped || equipItem.EvolvingExp < step.ReqExp)
        {
            owner.SendPacket(new SCItemElementResultPacket((long)equipItem.Id, oldLevel, oldLevel, false));
            owner.SendErrorMessage(ErrorMessageType.ItemCannotUse);
            skill.Cancelled = true;
            return;
        }

        // The element ladder carries its own price and labor cost - the skill itself has none, so
        // charging has to happen here rather than through the generic skill path.
        if (step.Tax > 0 && owner.Money < step.Tax)
        {
            owner.SendPacket(new SCItemElementResultPacket((long)equipItem.Id, oldLevel, oldLevel, false));
            owner.SendErrorMessage(ErrorMessageType.NotEnoughMoney);
            skill.Cancelled = true;
            return;
        }

        if (step.ConsumeLp > 0 && owner.LaborPower + owner.LocalLaborPower < step.ConsumeLp)
        {
            owner.SendPacket(new SCItemElementResultPacket((long)equipItem.Id, oldLevel, oldLevel, false));
            owner.SendErrorMessage(ErrorMessageType.NotEnoughLaborPower);
            skill.Cancelled = true;
            return;
        }

        if (step.Tax > 0)
            owner.SubtractMoney(SlotType.Inventory, step.Tax, ItemTaskType.SkillEffectConsumption);

        if (step.ConsumeLp > 0)
            owner.ChangeLabor((short)-step.ConsumeLp, skill.Template.ActabilityGroupId);

        equipItem.EvolvingExp -= step.ReqExp;
        equipItem.ElementLevel = nextLevel;
        equipItem.IsDirty = true;

        owner.SendPacket(new SCItemDetailUpdatedPacket(equipItem));
        owner.SendPacket(new SCItemElementResultPacket((long)equipItem.Id, oldLevel, nextLevel, true));

        Logger.Debug("ItemElement: {0} raised item {1} element {2} -> {3}",
            owner.Name, equipItem.Id, oldLevel, nextLevel);
    }
}
