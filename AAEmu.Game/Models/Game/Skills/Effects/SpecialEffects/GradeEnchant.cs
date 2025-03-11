using System;
using System.Collections.Generic;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
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

    private enum GradeEnchantResult
    {
        Break = 0,
        Downgrade = 1,
        Fail = 2,
        Success = 3,
        GreatSuccess = 4
    }

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
            if ((charm != null) && (charm.SupportItemId != 0))
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
        var gradeTemplate = ItemManager.Instance.GetGradeTemplate(item.Grade);

        var tasks = new List<ItemTask>();

        var cost = GoldCost(gradeTemplate, item, value3);
        if (cost == -1)
        {
            // No gold on template, invalid ?
            return;
        }

        if (character.Money < cost)
        {
            character.SendErrorMessage(ErrorMessageType.NotEnoughMoney);
            return;
        }

        if (!character.Inventory.CheckItems(SlotType.Bag, scroll.ItemTemplateId, 1))
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

            // tasksRemove.Add(InventoryHelper.GetTaskAndRemoveItem(character, charmItem, 1));
        }

        // All seems to be in order, roll item, consume items and send the results
        var result = RollRegrade(gradeTemplate, item, isLucky, useCharm, charmInfo);
        if (result == GradeEnchantResult.Break)
        {
            // Poof
            item._holdingContainer.RemoveItem(ItemTaskType.GradeEnchant, item, true);
        }
        else
        {
            // No Poof
            character.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.GradeEnchant, new List<ItemTask>() { new ItemGradeChange(item, item.Grade) }, new List<ulong>()));
        }

        // Consume
        character.SubtractMoney(SlotType.Bag, cost);
        // TODO: Handled by skill already, do more tests
        // character.Inventory.PlayerInventory.ConsumeItem(ItemTaskType.GradeEnchant, scroll.ItemTemplateId, 1, character.Inventory.GetItemById(scroll.ItemId));
        if (useCharm)
            character.Inventory.Bag.ConsumeItem(ItemTaskType.GradeEnchant, charmItem.TemplateId, 1, charmItem);

        character.SendPacket(new SCItemGradeEnchantResultPacket((byte)result, item, initialGrade, item.Grade));
        character.BroadcastPacket(new SCSkillEndedPacket(skill.TlId), true);

        // Let the world know if we got lucky enough
        if (item.Grade >= 8 && (result == GradeEnchantResult.Success || result == GradeEnchantResult.GreatSuccess))
        {
            WorldManager.Instance.BroadcastPacketToServer(
                new SCGradeEnchantBroadcastPacket(character.Name, (byte)result, item, initialGrade, item.Grade));
        }
    }

    private static GradeEnchantResult RollRegrade(GradeTemplate gradeTemplate, Item item, bool isLucky, bool useCharm,
        ItemGradeEnchantingSupport charmInfo)
    {
        var successRoll = Rand.Next(0, 10000);
        var breakRoll = Rand.Next(0, 10000);
        var downgradeRoll = Rand.Next(0, 10000);
        var greatSuccessRoll = Rand.Next(0, 10000);

        // TODO : Refactor
        var successChance = useCharm
            ? GetCharmChance(gradeTemplate.EnchantSuccessRatio, charmInfo.AddSuccessRatio, charmInfo.AddSuccessMul)
            : gradeTemplate.EnchantSuccessRatio;
        var greatSuccessChance = useCharm
            ? GetCharmChance(gradeTemplate.EnchantGreatSuccessRatio, charmInfo.AddGreatSuccessRatio,
                charmInfo.AddGreatSuccessMul)
            : gradeTemplate.EnchantGreatSuccessRatio;
        var breakChance = useCharm
            ? GetCharmChance(gradeTemplate.EnchantBreakRatio, charmInfo.AddBreakRatio, charmInfo.AddBreakMul)
            : gradeTemplate.EnchantBreakRatio;
        var downgradeChance = useCharm
            ? GetCharmChance(gradeTemplate.EnchantDowngradeRatio, charmInfo.AddDowngradeRatio,
                charmInfo.AddDowngradeMul)
            : gradeTemplate.EnchantDowngradeRatio;

        if (successRoll < successChance)
        {
            if (isLucky && greatSuccessRoll < greatSuccessChance)
            {
                // TODO : Refactor
                var increase = useCharm ? 2 + charmInfo.AddGreatSuccessGrade : 2;
                item.Grade = (byte)GetNextGrade(gradeTemplate, increase).Grade;
                return GradeEnchantResult.GreatSuccess;
            }

            item.Grade = (byte)GetNextGrade(gradeTemplate, 1).Grade;
            return GradeEnchantResult.Success;
        }

        if (breakRoll < breakChance)
        {
            return GradeEnchantResult.Break;
        }

        if (downgradeRoll < downgradeChance)
        {
            var newGrade = (byte)Rand.Next(gradeTemplate.EnchantDowngradeMin, gradeTemplate.EnchantDowngradeMax);
            if (newGrade < 0)
            {
                return GradeEnchantResult.Fail;
            }

            item.Grade = newGrade;
            return GradeEnchantResult.Downgrade;
        }

        return GradeEnchantResult.Fail;
    }

    private static int GoldCost(GradeTemplate gradeTemplate, Item item, int ItemType)
    {
        uint slotTypeId = 0;
        switch (ItemType)
        {
            case 1:
                WeaponTemplate weaponTemplate = (WeaponTemplate)item.Template;
                slotTypeId = weaponTemplate.HoldableTemplate.SlotTypeId;
                break;
            case 2:
                ArmorTemplate armorTemplate = (ArmorTemplate)item.Template;
                slotTypeId = armorTemplate.SlotTemplate.SlotTypeId;
                break;
            case 24:
                AccessoryTemplate accessoryTemplate = (AccessoryTemplate)item.Template;
                slotTypeId = accessoryTemplate.SlotTemplate.SlotTypeId;
                break;
        }

        if (slotTypeId == 0)
        {
            return -1;
        }

        var enchantingCost = ItemManager.Instance.GetEquipSlotEnchantingCost(slotTypeId);

        var itemGrade = gradeTemplate.EnchantCost;
        var itemLevel = item.Template.Level;
        var equipSlotEnchantCost = enchantingCost.Cost;

        var parameters = new Dictionary<string, double>();
        parameters.Add("item_grade", itemGrade);
        parameters.Add("item_level", itemLevel);
        parameters.Add("equip_slot_enchant_cost", equipSlotEnchantCost);
        var formula = FormulaManager.Instance.GetFormula((uint)FormulaKind.GradeEnchantCost);

        var cost = (int)formula.Evaluate(parameters);

        return cost;
    }

    private static GradeTemplate GetNextGrade(GradeTemplate currentGrade, int gradeChange)
    {
        return ItemManager.Instance.GetGradeTemplateByOrder(currentGrade.GradeOrder + gradeChange);
    }

    private static int GetCharmChance(int baseChance, int charmRatio, int charmMul)
    {
        return (baseChance + charmRatio) + (int)(baseChance * (charmMul / 100.0));
    }
}
