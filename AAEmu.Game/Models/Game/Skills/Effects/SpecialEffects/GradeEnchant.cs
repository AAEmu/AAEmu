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
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

/// <summary>
/// Regrade - the "Regrade" tab in the enchant window. Rolls an item one grade up the
/// <c>item_grades</c> table, or punishes the attempt.
/// </summary>
/// <remarks>
/// <para>
/// 10.0.2.13 moved this system's numbers twice over. The odds are no longer global columns on
/// <c>item_grades</c> but per-item rows in <c>item_enchant_ratios</c>, reached through
/// <see cref="ItemEnchantGameData.GetGradeEnchantRatio"/> - reading the old columns yielded zero for
/// every chance, so every attempt fell through to Fail. And the client's IGER_* constants gained a
/// Disable step, which pushed Fail, Success and GreatSuccess each one value up; see
/// <see cref="ItemGradeEnchantResult"/>.
/// </para>
/// <para>
/// <c>value1</c> marks the "shining" scrolls, which are the only ones that may overshoot by a grade.
/// <c>value3</c> is the <c>impl_id</c> of the gear the scroll is sold for - 1 weapons, 2 armor,
/// 24 accessories, 28 slave equipment, 30 mount armor - and doubles as the check that a weapon
/// scroll cannot be aimed at a breastplate. <c>value2</c> is set on two skills only (a test scroll
/// and an event scroll) and its meaning is not established, so it is left alone.
/// </para>
/// </remarks>
public class GradeEnchant : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.GradeEnchant;

    /// <summary>
    /// Grade from which a success is worth a server-wide notice. Kept as-is from the 1.2 server:
    /// grade 8 is where the shipped default group's success ratio first drops below 25%.
    /// </summary>
    private const byte BroadcastFromGrade = 8;

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
        if (caster is not Character character)
        {
            Logger.Error("GradeEnchant: caster {0} is not a character", caster?.Id);
            return;
        }

        if (casterObj is not SkillItem scroll)
        {
            Logger.Warn("GradeEnchant: caster object {0} is not an item", casterObj);
            skill.Cancelled = true;
            return;
        }

        if (targetObj is not SkillCastItemTarget itemTarget)
        {
            Logger.Warn("GradeEnchant: target {0} is not an item", targetObj);
            skill.Cancelled = true;
            return;
        }

        var item = character.Inventory.GetItemById(itemTarget.Id);
        if (item?.Template == null)
        {
            Logger.Warn("GradeEnchant: item {0} not found", itemTarget.Id);
            skill.Cancelled = true;
            return;
        }

        // The scroll knows what it is for. Without this a weapon scroll aimed at armor would be
        // priced off the armor's slot and still succeed.
        if (value3 != 0 && (int)item.Template.ImplId != value3)
        {
            character.SendErrorMessage(ErrorMessageType.ItemCannotUse);
            skill.Cancelled = true;
            return;
        }

        if (!item.Template.Gradable)
        {
            character.SendErrorMessage(ErrorMessageType.ItemCannotUse);
            skill.Cancelled = true;
            return;
        }

        // Read off Item rather than EquipItem: slave equipment and mount armor are regradable too
        // and do not all arrive as EquipItem, while the flag itself lives on every item.
        if (item.ItemFlags.HasFlag(ItemFlag.EnchantDisabled))
        {
            character.SendErrorMessage(ErrorMessageType.ItemCannotUse);
            skill.Cancelled = true;
            return;
        }

        var gradeTemplate = ItemManager.Instance.GetGradeTemplate(item.Grade);
        if (gradeTemplate == null)
        {
            Logger.Warn("GradeEnchant: item {0} sits at unknown grade {1}", item.Id, item.Grade);
            skill.Cancelled = true;
            return;
        }

        var maxGradeOrder = MaxGradeOrder(item);
        if (gradeTemplate.GradeOrder >= maxGradeOrder)
        {
            character.SendErrorMessage(ErrorMessageType.GradeEnchantMax);
            skill.Cancelled = true;
            return;
        }

        var ratio = ItemEnchantGameData.Instance.GetGradeEnchantRatio(
            item.TemplateId, item.Template.ImplId, item.Grade);
        if (ratio == null || ratio.IsTerminal)
        {
            // No row, or a row that can only fail: the top of the ladder in this item's group.
            character.SendErrorMessage(ErrorMessageType.GradeEnchantMax);
            skill.Cancelled = true;
            return;
        }

        // Charm ("보조석") - optional, and the window sends its item id in the skill object.
        ItemGradeEnchantingSupport charmInfo = null;
        Item charmItem = null;
        if (skillObject is SkillObjectItemGradeEnchantingSupport { SupportItemId: not 0 } charmObject)
        {
            charmItem = character.Inventory.GetItemById(charmObject.SupportItemId);
            if (charmItem == null)
            {
                Logger.Warn("GradeEnchant: charm {0} not found", charmObject.SupportItemId);
                skill.Cancelled = true;
                return;
            }

            charmInfo = ItemManager.Instance.GetItemGradEnchantingSupportByItemId(charmItem.TemplateId);
            if (charmInfo == null)
            {
                // An item was placed in the support slot that is not a charm at all. Previously this
                // dereferenced null one line later and took the whole cast down with it.
                Logger.Warn("GradeEnchant: item {0} (tpl {1}) is not an enchanting support",
                    charmItem.Id, charmItem.TemplateId);
                character.SendErrorMessage(ErrorMessageType.ItemCannotUse);
                skill.Cancelled = true;
                return;
            }

            if (charmInfo.RequireGradeMin != -1 && item.Grade < charmInfo.RequireGradeMin)
            {
                character.SendErrorMessage(ErrorMessageType.NotEnoughRequiredItem);
                skill.Cancelled = true;
                return;
            }

            if (charmInfo.RequireGradeMax != -1 && item.Grade > charmInfo.RequireGradeMax)
            {
                character.SendErrorMessage(ErrorMessageType.GradeEnchantMax);
                skill.Cancelled = true;
                return;
            }
        }

        if (!character.Inventory.CheckItems(SlotType.Inventory, scroll.ItemTemplateId, 1))
        {
            character.SendErrorMessage(ErrorMessageType.NotEnoughRequiredItem);
            skill.Cancelled = true;
            return;
        }

        // Priced before the roll so a player who cannot cover it keeps both their coin and scroll.
        var cost = GoldCost(item, ratio);
        if (cost > 0 && character.Money < cost)
        {
            character.SendErrorMessage(ErrorMessageType.NotEnoughMoney);
            skill.Cancelled = true;
            return;
        }

        var initialGrade = item.Grade;
        var result = Roll(ratio, item, maxGradeOrder, gradeTemplate, charmInfo, value1 != 0, out var itemBroken);

        if (itemBroken)
        {
            item._holdingContainer?.RemoveItem(ItemTaskType.GradeEnchant, item, true);
        }
        else
        {
            item.IsDirty = true;
            character.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.GradeEnchant,
                [new ItemGradeChange(item, item.Grade)], []));

            // The grade task carries only the grade. A lock-out lives in the flags byte instead, and
            // the tooltip's isEnchantDisable reads it from there, so that outcome needs the detail.
            if (result == ItemGradeEnchantResult.Disable)
                character.SendPacket(new SCItemDetailUpdatedPacket(item));
        }

        if (cost > 0)
            character.SubtractMoney(SlotType.Inventory, cost);

        // The scroll itself is consumed by the cast: these items carry use_skill_as_reagent, which
        // Skill.Use honours. Only the charm has to be taken here.
        if (charmItem != null)
            charmItem._holdingContainer?.ConsumeItem(ItemTaskType.GradeEnchant, charmItem.TemplateId, 1, charmItem);

        character.SendPacket(new SCGradeEnchantResultPacket(result, item, initialGrade, item.Grade));
        character.BroadcastPacket(new SCSkillEndedPacket(skill.TlId), true);

        if (!itemBroken && item.Grade >= BroadcastFromGrade &&
            result is ItemGradeEnchantResult.Success or ItemGradeEnchantResult.GreatSuccess)
        {
            WorldManager.Instance.BroadcastPacketToServer(
                new SCGradeEnchantBroadcastPacket(character.Name, (byte)result, item, initialGrade, item.Grade));
        }

        Logger.Debug("GradeEnchant: {0} item {1} grade {2} -> {3} ({4}), charm {5}, cost {6}",
            character.Name, item.Id, initialGrade, itemBroken ? initialGrade : item.Grade, result,
            charmItem?.TemplateId ?? 0, cost);
    }

    /// <summary>
    /// Highest <c>grade_order</c> the item may reach: its own ceiling where it has one, otherwise
    /// the top of the grade table.
    /// </summary>
    /// <remarks>
    /// <c>items.max_enchantable_grade</c> is a grade <em>id</em>, so it is resolved through the
    /// table rather than compared to an order directly - the two disagree for the bottom two grades.
    /// </remarks>
    private static int MaxGradeOrder(Item item)
    {
        var cap = item.Template?.MaxEnchantableGrade ?? -1;
        if (cap < 0)
            return ItemManager.MaxGradeValue;

        var capTemplate = ItemManager.Instance.GetGradeTemplate(cap);
        return capTemplate?.GradeOrder ?? ItemManager.MaxGradeValue;
    }

    /// <summary>
    /// Applies one attempt to <paramref name="item"/> and reports what the client should show.
    /// Every chance is per 10000.
    /// </summary>
    /// <remarks>
    /// The failure branch spends a single roll across break, disable and downgrade in that order, so
    /// the three shares cannot overlap and add up to at most one outcome. Rolling one number per
    /// outcome, as this used to, let a failed attempt both break and downgrade the same item.
    /// </remarks>
    private static ItemGradeEnchantResult Roll(ItemEnchantRatio ratio,
        Item item,
        int maxGradeOrder,
        GradeTemplate gradeTemplate,
        ItemGradeEnchantingSupport charm,
        bool greatAllowed,
        out bool itemBroken)
    {
        itemBroken = false;

        var successChance = WithCharm(ratio.SuccessRatio, charm?.AddSuccessRatio, charm?.AddSuccessMul);
        var greatChance = WithCharm(ratio.GreatSuccessRatio, charm?.AddGreatSuccessRatio, charm?.AddGreatSuccessMul);
        var breakChance = WithCharm(ratio.BreakRatio, charm?.AddBreakRatio, charm?.AddBreakMul);
        var disableChance = WithCharm(ratio.DisableRatio, charm?.AddDisableRatio, charm?.AddDisableMul);
        var downgradeChance = WithCharm(ratio.DowngradeRatio, charm?.AddDowngradeRatio, charm?.AddDowngradeMul);

        if (Random.Shared.Next(10000) < successChance)
        {
            // Only the shining scrolls may overshoot; the plain ones preview Great Success at 0%
            // even where the ratio row offers it.
            var step = greatAllowed && Random.Shared.Next(10000) < greatChance
                ? 2 + (charm?.AddGreatSuccessGrade ?? 0)
                : 1;

            var climbed = ClimbGrade(gradeTemplate, step, maxGradeOrder);
            if (climbed == null)
                return ItemGradeEnchantResult.Fail;

            item.Grade = (byte)climbed.Grade;
            return step > 1 ? ItemGradeEnchantResult.GreatSuccess : ItemGradeEnchantResult.Success;
        }

        var roll = Random.Shared.Next(10000);

        if (breakChance > 0 && roll < breakChance)
        {
            itemBroken = true;
            return ItemGradeEnchantResult.Break;
        }

        if (disableChance > 0 && roll < breakChance + disableChance)
        {
            item.ItemFlags |= ItemFlag.EnchantDisabled;
            return ItemGradeEnchantResult.Disable;
        }

        if (downgradeChance > 0 && roll < breakChance + disableChance + downgradeChance &&
            TryRollDowngrade(ratio, gradeTemplate, out var newGrade))
        {
            item.Grade = newGrade;
            return ItemGradeEnchantResult.Downgrade;
        }

        return ItemGradeEnchantResult.Fail;
    }

    /// <summary>
    /// The grade a failed attempt drops to, or false when this grade defines no downgrade.
    /// </summary>
    /// <remarks>
    /// <c>grade_enchant_downgrade_min</c> / <c>_max</c> are grade ids and both ends are inclusive -
    /// grade 8 in the default group drops to 5, 6 or 7. Low grades ship -1 on both, meaning a
    /// failure there costs nothing but the scroll. The old code fed those straight into
    /// <c>Random.Shared.Next</c> and cast the -1 it got back to a byte, which stamped grade 255 onto
    /// the item; its guard against that tested a byte for being negative and so never fired.
    /// </remarks>
    private static bool TryRollDowngrade(ItemEnchantRatio ratio, GradeTemplate gradeTemplate, out byte newGrade)
    {
        newGrade = 0;

        if (ratio.DowngradeMin < 0 || ratio.DowngradeMax < ratio.DowngradeMin)
            return false;

        var rolled = Random.Shared.Next(ratio.DowngradeMin, ratio.DowngradeMax + 1);
        var rolledTemplate = ItemManager.Instance.GetGradeTemplate(rolled);
        if (rolledTemplate == null)
            return false;

        // A "downgrade" that does not go down is a Fail as far as the player is concerned.
        if (rolledTemplate.GradeOrder >= gradeTemplate.GradeOrder)
            return false;

        newGrade = (byte)rolledTemplate.Grade;
        return true;
    }

    /// <summary>
    /// Steps <paramref name="step"/> grades up the table, stopping at <paramref name="maxGradeOrder"/>
    /// and settling for a smaller step rather than overshooting into a grade that does not exist.
    /// </summary>
    private static GradeTemplate ClimbGrade(GradeTemplate from, int step, int maxGradeOrder)
    {
        var targetOrder = Math.Min(from.GradeOrder + Math.Max(1, step), maxGradeOrder);

        while (targetOrder > from.GradeOrder)
        {
            var candidate = ItemManager.Instance.GetGradeTemplateByOrder(targetOrder);
            if (candidate != null)
                return candidate;
            targetOrder--;
        }

        return null;
    }

    /// <summary>
    /// Evaluates <c>grade_enchant_cost</c> (formula 22) for the attempt.
    /// </summary>
    /// <remarks>
    /// <c>item_grade</c> in the formula is not the grade but the ratio row's own cost factor, which
    /// is where this used to go wrong: it fed <see cref="GradeTemplate"/>'s unpopulated
    /// <c>EnchantCost</c>, i.e. zero. The formula also reads <c>grade_enchant_cost_mul</c>, which no
    /// shipped table supplies; leaving it out of the parameter set made the whole evaluation throw
    /// and fall back to a free regrade, so it is passed explicitly as neutral.
    /// </remarks>
    private static long GoldCost(Item item, ItemEnchantRatio ratio)
    {
        var formula = FormulaManager.Instance.GetFormula((uint)FormulaKind.GradeEnchantCost);
        if (formula == null)
            return 0;

        var slotTypeId = item.Template switch
        {
            WeaponTemplate weapon => weapon.HoldableTemplate?.SlotTypeId ?? 0,
            ArmorTemplate armor => armor.SlotTemplate?.SlotTypeId ?? 0,
            AccessoryTemplate accessory => accessory.SlotTemplate?.SlotTypeId ?? 0,
            _ => 0u
        };

        // Slave equipment and mount armor carry no wearable slot, so they simply pay the formula's
        // base term. Aborting the whole regrade over it, as this used to, made those scrolls inert.
        var slotCost = ItemManager.Instance.GetEquipSlotEnchantingCost(slotTypeId)?.Cost ?? 0;

        var parameters = new Dictionary<string, double>
        {
            { "item_grade", ratio.Cost },
            { "item_level", item.Template.Level },
            { "equip_slot_enchant_cost", slotCost },
            { "grade_enchant_cost_mul", 0 }
        };

        var cost = formula.Evaluate(parameters);
        if (double.IsNaN(cost) || cost <= 0)
            return 0;

        return cost > long.MaxValue ? long.MaxValue : (long)cost;
    }

    /// <summary>
    /// A charm's flat and proportional adjustment to one chance. Both are optional and both are
    /// signed - the break-suppressing charms ship a multiplier of -100.
    /// </summary>
    private static int WithCharm(int baseChance, int? addRatio, int? addMul)
    {
        if (addRatio == null && addMul == null)
            return baseChance;

        var adjusted = baseChance + (addRatio ?? 0) + (int)(baseChance * ((addMul ?? 0) / 100.0));
        return Math.Clamp(adjusted, 0, 10000);
    }
}
