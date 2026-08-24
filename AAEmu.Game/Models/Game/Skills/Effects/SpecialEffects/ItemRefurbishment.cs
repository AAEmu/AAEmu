using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Formulas;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

/// <summary>
/// Tempering - the "Tempering" tab in the enchant window, 연마 in the data. Pushes an item one step
/// up the <c>enchant_scale_ratios</c> ladder.
/// </summary>
/// <remarks>
/// <para>
/// This is the effect 10.0.2.13 actually ships (15 rows in <c>special_effects</c>, one per polish
/// item). The older <see cref="ItemCapScale"/> / <see cref="ItemCapScaleReset"/> pair it replaced has
/// no rows left in this build.
/// </para>
/// <para>
/// <c>value1</c> is the kind of gear the polish works on - 1 for weapons, 2 for armor - and is what
/// goes out in the result packet's untitled int. <c>value2</c> marks the "shining" variants and
/// <c>value4</c> carries 30, matching the top of the shipped ladder.
/// </para>
/// </remarks>
public class ItemRefurbishment : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.ItemRefurbishment;

    /// <summary>
    /// The first rung whose outcome is not a foregone conclusion, and so the first worth telling the
    /// whole server about.
    /// </summary>
    /// <remarks>
    /// Read off the ladder rather than picked: everything up to +9 succeeds outright, and the odds
    /// start falling at +10. A fixed threshold borrowed from regrade sat at +15, which no item can
    /// reach - <c>items.max_enchant_scale_id</c> is 12 wherever it is set at all - so the broadcast
    /// never fired once.
    /// </remarks>
    private static byte BroadcastFromScale()
    {
        for (byte scale = 0; scale < byte.MaxValue; scale++)
        {
            var ratio = ItemEnchantGameData.Instance.GetEnchantScaleRatio(scale);
            if (ratio == null)
                break;
            if (ratio.SuccessRatio < 10000)
                return (byte)(scale + 1);
        }

        return byte.MaxValue;
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
        if (caster is not Character owner)
        {
            Logger.Error("ItemRefurbishment: caster {0} is not a character", caster?.Id);
            return;
        }

        if (targetObj is not SkillCastItemTarget itemTarget)
        {
            Logger.Warn("ItemRefurbishment: target {0} is not an item", targetObj);
            skill.Cancelled = true;
            return;
        }

        if (owner.Inventory.GetItemById(itemTarget.Id) is not EquipItem equipItem)
        {
            Logger.Warn("ItemRefurbishment: item {0} not found or not equipment", itemTarget.Id);
            skill.Cancelled = true;
            return;
        }

        if (equipItem.EnchantDisabled)
        {
            owner.SendErrorMessage(ErrorMessageType.ItemCannotUse);
            skill.Cancelled = true;
            return;
        }

        // Two rules gate what may be tempered at all: the per-item ceiling and the explicit ban list.
        // Most banned items carry no ceiling either, but a couple do, so both have to be asked.
        var maxScale = equipItem.Template?.MaxEnchantScaleId ?? 0;
        if (maxScale == 0 || equipItem.EnchantScale >= maxScale ||
            ItemEnchantGameData.Instance.IsCapScaleForbidden(equipItem.TemplateId))
        {
            // Nothing left to gain. The client greys the button in this case, so this only catches a
            // stale window or a hand-built request.
            owner.SendErrorMessage(ErrorMessageType.GradeEnchantMax);
            skill.Cancelled = true;
            return;
        }

        var beforeScale = equipItem.EnchantScale;
        var ratio = ItemEnchantGameData.Instance.GetEnchantScaleRatio((byte)beforeScale);
        if (ratio == null)
        {
            Logger.Warn("ItemRefurbishment: no enchant_scale_ratios row for scale {0}", beforeScale);
            skill.Cancelled = true;
            return;
        }

        // The rung's own price, run through the ladder's cost formula. Taken before the roll so a
        // player who cannot cover it keeps both their coin and their polish.
        var cost = TemperCost(equipItem, beforeScale);
        if (cost > 0)
        {
            if (owner.Money < cost)
            {
                owner.SendErrorMessage(ErrorMessageType.NotEnoughMoney);
                skill.Cancelled = true;
                return;
            }

            // Booked the way regrade books its own charge. Naming a task type here instead leaves the
            // purse on screen untouched until the next relog, so the coin goes without the player
            // being shown it going.
            owner.SubtractMoney(SlotType.Inventory, cost);
        }

        // Only the shining polishes can overshoot, which is the one thing they are sold for: their
        // tooltip promises the +2 and the plain ones preview Great Success at a flat 0%. The ladder
        // itself offers the chance on every rung and cannot tell the two apart, so the polish does.
        var greatAllowed = value2 != 0;
        var result = Roll(ratio, equipItem, maxScale, greatAllowed, out var itemBroken);

        if (itemBroken)
        {
            equipItem._holdingContainer?.RemoveItem(ItemTaskType.ScaleCap, equipItem, true);
        }
        else
        {
            equipItem.IsDirty = true;
            // Published on its own packet rather than as an UpdateDetail task, which the client does
            // not decode as a detail and which leaves the item drawn as broken.
            owner.SendPacket(new SCItemDetailUpdatedPacket(equipItem));
        }

        // The tempering tab counts an attempt as settled only once a task of this exact type says
        // so - it is the one task number the client matches literally. The list stays empty on
        // purpose: the piece has already gone out on its own detail packet, and handing the same
        // blob over a second time is what leaves an item drawn as broken.
        owner.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.ScaleCap, [], []));

        var afterScale = itemBroken ? (ushort)0 : equipItem.EnchantScale;
        // The rung itself, which is what the window prints as "+N". The ladder's own scale column
        // runs ten times that - reporting it turned a +1 to +3 step into "+10 -> +30".
        owner.SendPacket(new SCItemRefurbishmentResultPacket(result, equipItem, value1,
            (short)beforeScale, (short)afterScale));

        // Only the eye-catching outcomes are worth a server-wide notice, the same way regrade only
        // broadcasts from a certain grade upward.
        if (!itemBroken && afterScale >= BroadcastFromScale() &&
            result is ItemGradeEnchantResult.Success or ItemGradeEnchantResult.GreatSuccess)
        {
            WorldManager.Instance.BroadcastPacketToServer(new SCScaleEnchantBroadcastPacket(
                owner.Name, result, equipItem, (short)beforeScale, (short)afterScale));
        }

        Logger.Debug("ItemRefurbishment: {0} item {1} {2} -> {3} ({4}), cost {5}",
            owner.Name, equipItem.Id, beforeScale, afterScale, result, cost);
    }

    /// <summary>
    /// Evaluates <c>enchant_scale_cost</c> (formula 59) for the rung being attempted.
    /// </summary>
    /// <remarks>
    /// Its inputs all come out of tables: <c>scale_cost</c> is the ladder rung's own cost, the slot
    /// factor is equip_slot_enchanting_costs for the piece's slot, and the level is the item's. No
    /// shipped table supplies <c>enchant_scale_cost_mul</c>, which leaves the multiplier neutral.
    /// </remarks>
    private static long TemperCost(EquipItem equipItem, ushort scale)
    {
        var ratio = ItemEnchantGameData.Instance.GetEnchantScaleRatio((byte)scale);
        var formula = FormulaManager.Instance.GetFormula((uint)FormulaKind.EnchantScaleCost);
        if (ratio == null || formula == null)
            return 0;

        var slotTypeId = equipItem.Template switch
        {
            WeaponTemplate weapon => weapon.HoldableTemplate?.SlotTypeId ?? 0,
            ArmorTemplate armor => armor.SlotTemplate?.SlotTypeId ?? 0,
            AccessoryTemplate accessory => accessory.SlotTemplate?.SlotTypeId ?? 0,
            _ => 0u
        };

        var slotCost = ItemManager.Instance.GetEquipSlotEnchantingCost(slotTypeId)?.Cost ?? 0;

        var parameters = new Dictionary<string, double>
        {
            { "item_level", equipItem.Template?.Level ?? 0 },
            { "scale_cost", ratio.Cost },
            { "equip_slot_enchant_cost", slotCost },
            { "enchant_scale_cost_mul", 0 }
        };

        var cost = formula.Evaluate(parameters);
        if (double.IsNaN(cost) || cost <= 0)
            return 0;

        return cost > long.MaxValue ? long.MaxValue : (long)cost;
    }

    /// <summary>
    /// Applies one step to <paramref name="equipItem"/> and reports what the client should show.
    /// The ratios are per 10000 and are read off the step the item is currently on.
    /// </summary>
    private static ItemGradeEnchantResult Roll(EnchantScaleRatio ratio, EquipItem equipItem, ushort maxScale, bool greatAllowed, out bool itemBroken)
    {
        itemBroken = false;

        if (Random.Shared.Next(10000) < ratio.SuccessRatio)
        {
            var step = greatAllowed && Random.Shared.Next(10000) < ratio.GreatSuccessRatio ? 2 : 1;
            var newScale = Math.Min(maxScale, equipItem.EnchantScale + step);
            equipItem.EnchantScale = (ushort)newScale;
            return step == 2 ? ItemGradeEnchantResult.GreatSuccess : ItemGradeEnchantResult.Success;
        }

        // Failure. The shipped ladder leaves break and disable at zero everywhere and only starts
        // applying a downgrade from +18 up, so below that a failed attempt costs nothing but the
        // material.
        var roll = Random.Shared.Next(10000);

        if (ratio.BreakRatio > 0 && roll < ratio.BreakRatio)
        {
            itemBroken = true;
            return ItemGradeEnchantResult.Break;
        }

        if (ratio.DisableRatio > 0 && roll < ratio.BreakRatio + ratio.DisableRatio)
        {
            equipItem.EnchantDisabled = true;
            return ItemGradeEnchantResult.Disable;
        }

        if (ratio.DownRatio > 0 && roll < ratio.BreakRatio + ratio.DisableRatio + ratio.DownRatio)
        {
            var down = Math.Max(1, (int)ratio.DownMax);
            equipItem.EnchantScale = (ushort)Math.Max(0, equipItem.EnchantScale - down);
            return ItemGradeEnchantResult.Downgrade;
        }

        return ItemGradeEnchantResult.Fail;
    }
}
