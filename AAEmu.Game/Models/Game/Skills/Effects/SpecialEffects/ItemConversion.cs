using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
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

        var requestedFamily = (uint)Math.Max(0, value1);
        var id = targetItem.TemplateId;
        var reagent = ItemConversionGameData.Instance.GetReagentForItem(
            targetItem.Grade,
            targetItem.Template.ImplId,
            id,
            targetItem.Template.Level,
            targetItem.Template.CategoryId,
            requestedFamily);
        if (reagent == null)
        {
            Logger.Error($"Couldn't find Reagent for item {id}");
            skill.Cancelled = true;
            return;
        }

        // value1 names the item_conv_sets family this effect performs. Refuse a cast that asks for another
        // family: an awakening effect must not run the disenchant chain off the same reagent pack.
        //
        // Only enforced when the item's own families are known. 10 of the 5519 reagent packs the content
        // references reach only conversions whose item_convs.item_conv_set_id is NULL - the origin-land
        // armour socket disenchants, covering 315 items - and rejecting on unknown data would break every
        // one of them.
        if (requestedFamily != 0 && reagent.HasKnownFamily && !ItemConversionGameData.Instance.IsValidConversionSet(value1, reagent))
        {
            Logger.Warn(
                "ItemConversion: skill {0} asked for conversion set {1} but item {2} belongs to set(s) {3}",
                skill.Template?.Id, value1, id, string.Join(',', reagent.ConversionFamilies.Order()));
            skill.Cancelled = true;
            return;
        }

        if (!ItemConversionGameData.Instance.TryRollProducts(reagent, requestedFamily, out var rolls))
        {
            Logger.Error($"Couldn't find Product from Reagent for item {id}");
            skill.Cancelled = true;
            return;
        }

        if (!TryGrant(character, skill, targetItem, id, reagent, rolls))
            return;
    }

    /// <summary>
    /// Grants every rolled reward and removes the reagent item in one operation.
    /// </summary>
    /// <remarks>
    /// Adding the rewards one at a time left a partial payout when a later one did not fit: conversion 6280
    /// with a single free slot handed over the non-stackable 15596, then failed on 34983 x50 and returned
    /// before the input was consumed, so the player kept item 49513 as well. Planning the credits and the
    /// debit on the bag together makes it all or nothing.
    /// </remarks>
    private static bool TryGrant(Character character, Skill skill, Item targetItem, uint targetItemId,
        ItemConversionReagent reagent, IReadOnlyList<ItemConversionRoll> rolls)
    {
        var requests = new List<ItemAcquisitionRequest>();
        foreach (var roll in rolls)
        {
            if (roll.ChanceFailed || roll.Count <= 0)
            {
                Logger.Debug("ItemConversion: item {0} (pack {1}) rolled no product", targetItemId, reagent.ReagentPackId);
                continue;
            }

            var template = ItemManager.Instance.GetTemplate(roll.Product.OutputItemId);
            if (template == null)
            {
                // Four item_conv_products rows name an item this content set does not define.
                Logger.Warn(
                    "ItemConversion: item {0} rolls product {1}, which has no item template",
                    targetItemId, roll.Product.OutputItemId);
                continue;
            }

            requests.Add(new ItemAcquisitionRequest(
                roll.Product.OutputItemId,
                roll.Count,
                ResolveGrade(template, roll.Product.GradeId)));
        }

        var inventory = character.Inventory;
        if (inventory?.Bag == null)
        {
            skill.Cancelled = true;
            return false;
        }

        ItemConsumptionPublication consumptionPublication = null;
        ItemAcquisitionPublication acquisitionPublication = null;
        using (PersistenceOperationScope.Enter())
        lock (inventory.MutationSyncRoot)
        {
            ItemAcquisitionPlan acquisition = null;
            try
            {
                if (requests.Count > 0 &&
                    !inventory.TryPlanBagAcquisition(ItemManager.Instance, requests, DateTime.UtcNow, out acquisition))
                {
                    skill.Cancelled = true;
                    character.SendErrorMessage(ErrorMessageType.BagFull);
                    return false;
                }

                if (!inventory.TryPlanExactBagConsumption(targetItem.Id, 1, out var consumption))
                {
                    Logger.Warn("ItemConversion: item {0} has no exact bag stack to consume", targetItemId);
                    skill.Cancelled = true;
                    return false;
                }

                var snapshots = new List<ItemPersistenceSnapshot>();
                if (acquisition != null)
                    snapshots.AddRange(acquisition.CapturePersistenceSnapshots());
                snapshots.AddRange(consumption.CapturePersistenceSnapshots(ItemManager.Instance));

                using (var connection = MySQL.CreateConnection())
                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        ItemManager.Instance.PersistSnapshots(connection, transaction, snapshots);
                        transaction.Commit();
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }

                if (acquisition != null)
                {
                    acquisition.MarkCommitted();
                    acquisitionPublication = acquisition.ApplyCommitted(ItemTaskType.Conversion);
                    acquisitionPublication.PublishPackets();
                }

                consumptionPublication = consumption.ApplyCommitted(ItemTaskType.Conversion);
                consumptionPublication.PublishPackets();
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "ItemConversion: failed to commit the conversion of item {0}", targetItemId);
                skill.Cancelled = true;
                return false;
            }
            finally
            {
                acquisition?.Dispose();
            }
        }

        // Quest and container callbacks run once the inventory and persistence guards are released.
        try
        {
            acquisitionPublication?.PublishCallbacks();
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "ItemConversion: failed to publish acquisition callbacks for item {0}", targetItemId);
        }

        try
        {
            consumptionPublication?.PublishCallbacks();
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "ItemConversion: failed to publish consumption callbacks for item {0}", targetItemId);
        }

        return true;
    }

    /// <summary>
    /// Grade an acquired stack gets, matching what <c>AcquireDefaultItem</c> derives from a grade of -1: the
    /// product's own <c>item_grade_id</c> when it has one, otherwise the template's fixed grade.
    /// </summary>
    private static byte ResolveGrade(Items.Templates.ItemTemplate template, int productGradeId)
    {
        if (productGradeId > 0)
            return (byte)Math.Min(productGradeId, byte.MaxValue);

        return (byte)Math.Clamp(template.FixedGrade, 0, byte.MaxValue);
    }
}
