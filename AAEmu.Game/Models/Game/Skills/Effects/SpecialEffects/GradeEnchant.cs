using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Formulas;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class GradeEnchant : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.GradeEnchant;

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
        if (caster is Character) { Logger.Debug("Special effects: GradeEnchant value1 {0}, value2 {1}, value3 {2}, value4 {3}", value1, value2, value3, value4); }

        // Get Player
        if (caster is not Character character || character is null)
        {
            return;
        }

        // Get Regrade Scroll Item
        if (casterObj is not SkillItem scroll || scroll is null)
        {
            return;
        }

        // Get Item to regrade
        if (targetObj is not SkillCastItemTarget itemTarget || itemTarget is null)
        {
            return;
        }

        // Check Charm
        var useCharm = false;
        SkillObjectItemGradeEnchantingSupport charm = null;
        if (skillObject is SkillObjectItemGradeEnchantingSupport)
        {
            charm = (SkillObjectItemGradeEnchantingSupport)skillObject;
            if (charm != null && charm.SupportItemId != 0)
            {
                useCharm = true;
            }
        }

        var isLucky = value1 != 0;
        var item = character.Inventory.GetItemById(itemTarget.Id);
        if (item == null)
        {
            // Invalid item
            return;
        }
        var initialGrade = item.Grade;
        var gradeTemplate = ItemManager.Instance.GetGradeTemplate(initialGrade);

        // Regade data for this item at its current grade (10.0.2.13 moved the odds out of item_grades)
        var ratio = ItemEnchantRatioGameData.Instance.GetRatio(item.TemplateId, item.Template, initialGrade);
        if (ratio == null)
        {
            Logger.Warn("GradeEnchant: no enchant ratios for item {0} at grade {1}", item.TemplateId, initialGrade);
            return;
        }

        // Items must be gradable and their grade reachable by scrolls (mythic/arche rows have upgrade_ratio 0)
        if (!item.Template.Gradable || gradeTemplate == null || gradeTemplate.UpgradeRatio <= 0)
        {
            character.SendErrorMessage(ErrorMessageType.GradeEnchantMax);
            return;
        }

        // Per-item hard cap (items.max_enchantable_grade, -1 = uncapped)
        var maxEnchantableGrade = item.Template.MaxEnchantableGrade;
        if (maxEnchantableGrade >= 0 && initialGrade >= maxEnchantableGrade)
        {
            character.SendErrorMessage(ErrorMessageType.GradeEnchantMax);
            return;
        }

        var tasks = new List<ItemTask>();

        var cost = GoldCost(ratio, item, value3);
        if (cost == -1)
        {
            // No slot enchant cost for this item type, invalid ?
            return;
        }

        if (character.Money < cost)
        {
            character.SendErrorMessage(ErrorMessageType.NotEnoughMoney);
            return;
        }

        if (!character.Inventory.CheckItems(SlotType.Inventory, scroll.ItemTemplateId, 1))
        {
            // No scroll
            character.SendErrorMessage(ErrorMessageType.NotEnoughRequiredItem);
            return;
        }

        ItemGradeEnchantingSupport charmInfo = null;
        Item charmItem = null;
        if (useCharm)
        {
            charmItem = character.Inventory.GetItemById(charm.SupportItemId);
            if (charmItem == null)
            {
                return;
            }

            charmInfo = ItemManager.Instance.GetItemGradEnchantingSupportByItemId(charmItem.TemplateId);
            if (charmInfo.RequireGradeMin != -1 && item.Grade < charmInfo.RequireGradeMin)
            {
                character.SendErrorMessage(ErrorMessageType.NotEnoughRequiredItem);
                return;
            }

            if (charmInfo.RequireGradeMax != -1 && item.Grade > charmInfo.RequireGradeMax)
            {
                character.SendErrorMessage(ErrorMessageType.GradeEnchantMax);
                return;
            }
        }

        // All seems to be in order, roll item, consume items and send the results
        var result = RollRegrade(ratio, gradeTemplate, isLucky, useCharm, charmInfo, out var newGrade);
        switch (result)
        {
            case GradeEnchantResult.Break:
            {
                // Poof
                item._holdingContainer.RemoveItem(ItemTaskType.GradeEnchant, item, true);
                break;
            }
            case GradeEnchantResult.Disable:
            {
                // Item survives but cannot be used until restored
                item.SetFlag(ItemFlag.Disabled);
                character.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.GradeEnchant, [new ItemUpdate(item)], []));
                break;
            }
            default:
            {
                if (newGrade != null)
                    item.Grade = (byte)newGrade.Grade;

                // No Poof
                character.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.GradeEnchant, [new ItemGradeChange(item, item.Grade)], []));
                break;
            }
        }

        // Consume
        character.SubtractMoney(SlotType.Inventory, cost);
        // TODO: Handled by skill already, do more tests
        // character.Inventory.PlayerInventory.ConsumeItem(ItemTaskType.GradeEnchant, scroll.ItemTemplateId, 1, character.Inventory.GetItemById(scroll.ItemId));
        if (useCharm)
            character.Inventory.Bag.ConsumeItem(ItemTaskType.GradeEnchant, charmItem.TemplateId, 1, charmItem);

        character.SendPacket(new SCGradeEnchantResultPacket((byte)result, item, initialGrade, item.Grade));
        character.BroadcastPacket(new SCSkillEndedPacket(skill.TlId), true);

        // Let the world know if we got lucky enough
        if (item.Grade >= 8 && (result == GradeEnchantResult.Success || result == GradeEnchantResult.GreatSuccess))
        {
            WorldManager.Instance.BroadcastPacketToServer(
                new SCGradeEnchantBroadcastPacket(character.Name, (byte)result, item, initialGrade, item.Grade));
        }
    }

    private static GradeEnchantResult RollRegrade(ItemEnchantRatio ratio, GradeTemplate currentGrade, bool isLucky,
        bool useCharm, ItemGradeEnchantingSupport charmInfo, out GradeTemplate newGrade)
    {
        newGrade = null;

        ItemGradeEnchantRules.CharmAdjustment? charm = null;
        if (useCharm && charmInfo != null)
        {
            charm = new ItemGradeEnchantRules.CharmAdjustment(
                charmInfo.AddSuccessRatio, charmInfo.AddSuccessMul,
                charmInfo.AddGreatSuccessRatio, charmInfo.AddGreatSuccessMul,
                charmInfo.AddBreakRatio, charmInfo.AddBreakMul,
                charmInfo.AddDowngradeRatio, charmInfo.AddDowngradeMul);
        }

        // One roll, prioritized checks (success/great -> break -> downgrade -> disable -> fail),
        // matching how the shipped odds tables behave (crafted groups disable instead of breaking,
        // artifact+ rows break instead of downgrading).
        var roll = Random.Shared.Next(0, ItemGradeEnchantRules.MaxRatio);
        var (result, newGradeOrder) = ItemGradeEnchantRules.Resolve(
            ratio, currentGrade, roll, isLucky, charm,
            grade => ItemManager.Instance.GetGradeTemplate((byte)grade)?.GradeOrder ?? -1);

        newGrade = ItemManager.Instance.GetGradeTemplateByOrder(newGradeOrder) ?? currentGrade;
        return result;
    }

    private static int GoldCost(ItemEnchantRatio ratio, Item item, int ItemType)
    {
        uint slotTypeId = 0;
        switch (ItemType)
        {
            case 1:
                var weaponTemplate = (WeaponTemplate)item.Template;
                slotTypeId = weaponTemplate.HoldableTemplate.SlotTypeId;
                break;
            case 2:
                var armorTemplate = (ArmorTemplate)item.Template;
                slotTypeId = armorTemplate.SlotTemplate.SlotTypeId;
                break;
            case 24:
                var accessoryTemplate = (AccessoryTemplate)item.Template;
                slotTypeId = accessoryTemplate.SlotTemplate.SlotTypeId;
                break;
        }

        if (slotTypeId == 0)
        {
            return -1;
        }

        var enchantingCost = ItemManager.Instance.GetEquipSlotEnchantingCost(slotTypeId);

        var itemGrade = ratio.Cost;
        var itemLevel = item.Template.Level;
        var equipSlotEnchantCost = enchantingCost.Cost;

        var parameters = new Dictionary<string, double>
        {
            { "item_grade", itemGrade },
            { "item_level", itemLevel },
            { "equip_slot_enchant_cost", equipSlotEnchantCost }
        };
        var formula = FormulaManager.Instance.GetFormula((uint)FormulaKind.GradeEnchantCost);

        var cost = (int)formula.Evaluate(parameters);

        return cost;
    }
}
