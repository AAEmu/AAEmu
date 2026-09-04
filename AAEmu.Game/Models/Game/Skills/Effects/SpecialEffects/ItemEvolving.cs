using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Formulas;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

/// <summary>
/// Synthesis - the "Synthesis" tab, 합성 in the data. Feeds infusions to a piece of gear, which banks
/// their experience and steps its grade up for as long as that experience covers the next step.
/// </summary>
/// <remarks>
/// <para>
/// Every quantity comes out of the tables, none of it out of this file:
/// </para>
/// <list type="bullet">
/// <item>What a material is worth is <c>gain_exp</c> on <em>its own</em> pool at <em>its own</em>
/// grade - not the target's. A Rank 1 infusion sits at grade 2 of pool 672, which carries 50.</item>
/// <item>What a step costs is <c>grade_exp</c> on the <em>target's</em> pool at the grade the item is
/// leaving. <c>req_exp</c> looks like the obvious candidate and is zero across every shipped row.</item>
/// <item>What the attempt charges is the experience laid across the grades it travels, each slice
/// billed at that grade's own <c>gold_mul</c> per mille and the banked experience deducted once,
/// then run through formula 64 with the item's level.</item>
/// <item>Which materials a pool accepts is a question about category <em>groups</em>, answered by
/// item_rnd_attr_category_relations.</item>
/// </list>
/// <para>
/// An item names its pool in one of two places depending on what it is: an infusion registered as
/// <c>evolving_material</c> names it in item_evolving_materials, while anything the equipment tables
/// know - including infusions filed as armor, which the shipped Story Quest ones are - names it on
/// its own row there. Both have to be asked or half the materials in the game look inert.
/// </para>
/// </remarks>
public class ItemEvolving : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.ItemEvolving;

    /// <summary>
    /// Ceiling on the change attempts an item can bank. One is granted per grade gained, and the
    /// tooltip tops out at five however far a piece is pushed.
    /// </summary>
    private const ushort MaxChangeAttempts = 5;

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
            Logger.Error("ItemEvolving: caster {0} is not a character", caster?.Id);
            return;
        }

        if (targetObj is not SkillCastItemTarget itemTarget)
        {
            Logger.Warn("ItemEvolving: target {0} is not an item", targetObj);
            skill.Cancelled = true;
            return;
        }

        if (owner.Inventory.GetItemById(itemTarget.Id) is not EquipItem equipItem)
        {
            Logger.Warn("ItemEvolving: item {0} not found or not equipment", itemTarget.Id);
            skill.Cancelled = true;
            return;
        }

        if (equipItem.EnchantDisabled)
        {
            owner.SendErrorMessage(ErrorMessageType.ItemCannotUse);
            skill.Cancelled = true;
            return;
        }

        var categoryId = GetCategoryId(equipItem);
        var category = ItemEnchantGameData.Instance.GetRndAttrCategory(categoryId);
        if (category == null)
        {
            owner.SendErrorMessage(ErrorMessageType.ItemCannotUse);
            skill.Cancelled = true;
            return;
        }

        var materials = ResolveMaterials(owner, equipItem, categoryId, category, skillObject);
        if (materials.Count == 0)
        {
            // Either the window sent nothing usable or every slot failed a rule. Refuse before
            // anything is charged - the labor cost in EndSkill keys off Cancelled too.
            owner.SendErrorMessage(ErrorMessageType.ItemCannotUse);
            skill.Cancelled = true;
            return;
        }

        var addExp = 0u;
        var bonusExp = 0u;
        foreach (var (_, materialProperty) in materials)
        {
            addExp += materialProperty.GainExp;
            bonusExp += RollBonusExp(materialProperty);
        }

        // Labor is priced per infusion, not per cast: two in the slots cost twice one. The skill's
        // consume_lp is that per-infusion price, so the count goes to the generic charge in EndSkill
        // rather than being taken here.
        skill.LaborUnits = materials.Count;
        var laborCost = skill.Template.ConsumeLaborPower * materials.Count;
        if (laborCost > 0 && owner.LaborPower + owner.LocalLaborPower < laborCost)
        {
            owner.SendErrorMessage(ErrorMessageType.NotEnoughLaborPower);
            skill.Cancelled = true;
            return;
        }

        // Only the experience the ladder can still absorb is paid for. Past the top rung there is
        // nothing left to buy, and the window says so itself - it reports the rest as overflow - so
        // charging on the full amount bills the player for experience that is discarded on arrival.
        var offered = addExp + bonusExp;
        var room = ItemEnchantGameData.Instance.GetExpToMaxGrade(categoryId, equipItem.Grade, equipItem.EvolvingExp);
        if (!ItemEvolvingRules.TryPurchase(offered, room, out var purchased))
        {
            owner.SendErrorMessage(ErrorMessageType.ItemCannotUse);
            skill.Cancelled = true;
            return;
        }

        // The charge is for the experience this attempt buys, and it is taken before anything is
        // granted so a player who cannot afford it keeps both their coin and their infusions.
        var cost = EvolvingCost(owner, category, equipItem, purchased);
        if (cost > 0 && !TryCharge(owner, category, cost))
        {
            owner.SendErrorMessage(ErrorMessageType.NotEnoughMoney);
            skill.Cancelled = true;
            return;
        }

        // Reported as the skill's own reagents, which is the task the synthesis tab watches to learn
        // its cast is over. Nothing else releases it: the infusions are spent by this effect rather
        // than by the skill engine, so without this the tab never saw a reagent task at all.
        foreach (var (materialItem, _) in materials)
            materialItem._holdingContainer?.ConsumeItem(ItemTaskType.SkillReagents, materialItem.TemplateId, 1, materialItem);

        var beforeGrade = equipItem.Grade;
        equipItem.EvolvingExp += purchased;
        var newAttributes = ApplyExperience(equipItem, category, categoryId);
        equipItem.IsDirty = true;

        // The synthesis grade is the item's own grade, so a step has to be published as one on top of
        // the detail refresh - the client redraws the name colour and the stat block off it.
        // The detail goes out on its own packet: the UpdateDetail task carries an array the client
        // does not decode as a detail, which leaves the item drawn as broken until a relog.
        if (equipItem.Grade != beforeGrade)
        {
            owner.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.Evolving,
                [new ItemGradeChange(equipItem, equipItem.Grade)], []));
        }

        owner.SendPacket(new SCItemDetailUpdatedPacket(equipItem));

        // Gear bonuses are totalled when a piece is equipped and not again, so a change made to a
        // piece already being worn never reached the character sheet. Re-total here; the piece is
        // only in the sum at all while it sits in the equipment container.
        if (equipItem.SlotType == SlotType.Equipment)
            owner.UpdateGearBonuses(null, null);

        // The result carries the grades themselves, new one first - not a success code and not a
        // "did it step" flag. Equal grades are the quiet outcome for an attempt that only banked
        // experience, so they go out unchanged rather than being suppressed.
        owner.SendPacket(new SCItemEvolvingResultPacket(equipItem.Id, equipItem.Grade, beforeGrade,
            addExp, bonusExp, 0, newAttributes));

        Logger.Debug("ItemEvolving: {0} fed {1} material(s) to item {2} for {3}(+{4}) exp, {9} of it spent, grade {5} -> {6}, cost {7}, labor {8}",
            owner.Name, materials.Count, equipItem.Id, addExp, bonusExp, beforeGrade, equipItem.Grade, cost, laborCost, purchased);
    }

    /// <summary>
    /// The synthesis pool an item belongs to. Materials filed as <c>evolving_material</c> name it in
    /// item_evolving_materials; everything the equipment tables know names it on its own row.
    /// </summary>
    private static uint GetCategoryId(Item item)
    {
        var declared = ItemEnchantGameData.Instance.GetEvolvingMaterialCategory(item.TemplateId);
        if (declared != 0)
            return declared;

        return (item.Template as EquipItemTemplate)?.RndAttrCategoryId ?? 0;
    }

    /// <summary>
    /// Turns the window's material slots into the items they name, dropping anything that fails a
    /// rule. Everything is settled here so the consume phase afterwards cannot half-succeed.
    /// </summary>
    private static List<(Item Item, ItemRndAttrCategoryProperty Property)> ResolveMaterials(
        Character owner, EquipItem targetItem, uint targetCategoryId, ItemRndAttrCategory targetCategory,
        SkillObject skillObject)
    {
        var resolved = new List<(Item, ItemRndAttrCategoryProperty)>();

        if (skillObject is not SkillObjectItemEvolvingMaterials materials)
        {
            Logger.Warn("ItemEvolving: cast carried no material slots ({0})", skillObject?.GetType().Name);
            return resolved;
        }

        var seen = new HashSet<ulong>();
        foreach (var itemId in materials.UsedMaterialItemIds)
        {
            // The same id twice would be consumed twice off one stack of one.
            if (!seen.Add(itemId) || itemId == targetItem.Id)
                continue;

            var item = owner.Inventory.Bag.GetItemByItemId(itemId);
            if (item == null)
            {
                Logger.Warn("ItemEvolving: {0} named material {1} that is not in their bag", owner.Name, itemId);
                continue;
            }

            var materialCategoryId = GetCategoryId(item);
            if (materialCategoryId == 0 ||
                !ItemEnchantGameData.Instance.AcceptsMaterial(targetCategoryId, materialCategoryId))
            {
                Logger.Warn("ItemEvolving: material {0} (pool {1}) does not feed pool {2}",
                    item.TemplateId, materialCategoryId, targetCategoryId);
                continue;
            }

            // A pool can refuse materials above a grade. 255 is the shipped "no limit", and the odd
            // negative row is read the same way rather than as a limit that refuses everything.
            if (targetCategory.MaterialGradeLimit is >= 0 and < 255 && item.Grade > targetCategory.MaterialGradeLimit)
            {
                Logger.Warn("ItemEvolving: material {0} at grade {1} is above pool {2}'s limit {3}",
                    item.TemplateId, item.Grade, targetCategoryId, targetCategory.MaterialGradeLimit);
                continue;
            }

            var property = ItemEnchantGameData.Instance.GetRndAttrProperty(materialCategoryId, item.Grade);
            if (property == null || property.GainExp == 0)
            {
                Logger.Warn("ItemEvolving: material {0} is worth no experience at grade {1} of pool {2}",
                    item.TemplateId, item.Grade, materialCategoryId);
                continue;
            }

            resolved.Add((item, property));
        }

        return resolved;
    }

    /// <summary>
    /// Spends banked experience on as many grade steps as it covers, granting what each grade
    /// reached allows.
    /// </summary>
    /// <remarks>
    /// What an item holds is its progress inside the current grade, which the client prints straight
    /// against that grade's cost ("Current XP 520/230") - so a step subtracts the cost and carries
    /// the remainder up. Treating the value as a running total instead leaves the grade standing
    /// while experience piles past the bar, which is what a reading over 100% means.
    /// </remarks>
    private static List<ItemRndAttrUnitModifier> ApplyExperience(EquipItem equipItem,
        ItemRndAttrCategory category, uint categoryId)
    {
        var added = new List<ItemRndAttrUnitModifier>();

        while (true)
        {
            var next = NextGrade(equipItem.Grade);
            if (next == null)
                break;

            // A rung is priced by the grade it leads into, not by the one it leaves. A grade the pool
            // never priced is a grade the pool does not offer, and that is where its ladder ends -
            // which is how one pool stops at Celestial and the next carries on to Eternal.
            var needed = ItemEnchantGameData.Instance.GetGradeExp(categoryId, next.Value);
            if (needed == 0)
            {
                // Unless the pool's own ceiling reaches past its priced rungs, which is how the
                // story-quest sets are built: their last step carries no price of its own and is
                // paid for by filling the grade below it. That is the jump those pieces make from a
                // full bar, and it is the only way an unpriced rung can be entered.
                if (!AllowsFreeStep(category, next.Value))
                    break;

                needed = ItemEnchantGameData.Instance.GetGradeExp(categoryId, equipItem.Grade);
                if (needed == 0)
                    break;
            }

            if (equipItem.EvolvingExp < needed)
                break;

            equipItem.EvolvingExp -= needed;
            equipItem.Grade = next.Value;

            if (equipItem.EvolveChance < MaxChangeAttempts)
                equipItem.EvolveChance++;

            added.AddRange(TopUpAttributes(equipItem, categoryId));
        }

        var beyond = NextGrade(equipItem.Grade);
        var atTop = beyond == null ||
                    (ItemEnchantGameData.Instance.GetGradeExp(categoryId, beyond.Value) == 0 &&
                     !AllowsFreeStep(category, beyond.Value));

        if (atTop)
        {
            // The bar still fills at the top, it just cannot tip over. Experience past the last
            // rung's own cost is overflow the pool has nothing to sell for, so it is dropped rather
            // than left on the item to read past 100%.
            var ceiling = ItemEnchantGameData.Instance.GetGradeExp(categoryId, equipItem.Grade);
            if (ceiling > 0 && equipItem.EvolvingExp > ceiling)
                equipItem.EvolvingExp = ceiling;
        }
        else
        {
            added.AddRange(TopUpAttributes(equipItem, categoryId));
        }

        return added;
    }

    /// <summary>
    /// Whether a pool may enter a grade it never priced, on the strength of its own ceiling.
    /// </summary>
    /// <remarks>
    /// Most pools price every grade they offer and their ceiling sits at or below the last of them.
    /// The story-quest sets are the other way round - the ceiling names one grade more than the
    /// ladder prices - and that spare grade is the step those pieces take once the grade below it is
    /// full.
    /// </remarks>
    private static bool AllowsFreeStep(ItemRndAttrCategory category, byte grade)
    {
        return category != null && category.MaxEvolvingGrade >= 0 && grade <= category.MaxEvolvingGrade;
    }

    /// <summary>
    /// The grade one step up, in the order the client lists them, or null at the end of the list.
    /// </summary>
    /// <remarks>
    /// Not simply the next id. Crude is id 1 but sits below Basic at id 0, so counting ids upward
    /// walks an item at Basic backwards into Crude - which is why item_grades carries a running
    /// order alongside the id.
    /// </remarks>
    private static byte? NextGrade(byte grade)
    {
        var current = ItemManager.Instance.GetGradeTemplate(grade);
        if (current == null)
            return null;

        var next = ItemManager.Instance.GetGradeTemplateByOrder(current.GradeOrder + 1);
        return next == null ? null : (byte)next.Grade;
    }

    /// <summary>
    /// Draws the effect groups an item is still owed at its grade. The lower rungs of a pool grant
    /// none at all, which is why a freshly synthesised piece can climb several grades before its
    /// first effect appears.
    /// </summary>
    /// <remarks>
    /// One draw, not a draw per empty slot. Each bundle is asked once and answers with what it has
    /// left over what the piece already holds; drawing again for the next slot would let a bundle
    /// that allows a single main stat hand out a second one on the following pass, which is how a
    /// weapon ended up carrying both Spirit and Intelligence out of the same one-pick bundle.
    /// </remarks>
    internal static List<ItemRndAttrUnitModifier> TopUpAttributes(EquipItem equipItem, uint categoryId)
    {
        var added = new List<ItemRndAttrUnitModifier>();
        var cap = ItemEnchantGameData.Instance.GetRndAttrCap(categoryId, equipItem.Grade);
        var groupIds = equipItem.RndAttrGroupIds ?? new uint[EquipItem.RndAttrSlots];
        var used = groupIds.Count(id => id != 0);
        if (used >= cap)
            return added;

        var held = equipItem.UsedRndAttrGroupIds.ToList();
        foreach (var groupId in ItemEnchantGameData.Instance.RollRndAttrGroups(categoryId, equipItem.Grade, held))
        {
            if (used >= cap)
                break;
            groupIds[used++] = groupId;

            var modifier = ItemEnchantGameData.Instance.GetRndAttrModifier(groupId, equipItem.Grade, equipItem.EvolvingExp);
            if (modifier != null)
                added.Add(modifier);
        }

        equipItem.RndAttrGroupIds = groupIds;
        return added;
    }

    /// <summary>Bonus experience a material may carry on top of its base worth.</summary>
    private static uint RollBonusExp(ItemRndAttrCategoryProperty property)
    {
        if (property.BonusExpChance <= 0 || Random.Shared.Next(10000) >= property.BonusExpChance)
            return 0;

        var min = Math.Min(property.BonusExpMin, property.BonusExpMax);
        var max = Math.Max(property.BonusExpMin, property.BonusExpMax);
        // The range is a percentage of what the material is worth, which is why the shipped default
        // for both ends is 100.
        var percent = max > min ? Random.Shared.Next(min, max + 1) : min;
        return (uint)Math.Max(0, property.GainExp * percent / 100);
    }

    /// <summary>
    /// What the window quotes for an attempt, and so what it has to cost.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The price is not a multiple of the experience bought. The experience is laid across the
    /// grades it will pass through, and each grade charges for its own slice at its own
    /// <c>gold_mul</c>, which is a per-mille rate. Experience the piece already banked is free -
    /// it has been paid for once - so only the first slice deducts it.
    /// </para>
    /// <para>
    /// The walk follows <c>item_grades.grade_order</c>, not the numeric grade, and stops at the
    /// pool's <c>max_evolving_grade</c>: the top grade is billed and the walk ends there, so
    /// overflow experience past the ladder costs nothing. That is the same overflow the window
    /// prints beside the bar.
    /// </para>
    /// <para>
    /// Only then does formula 64 run, and it takes the accumulated price as its quantity. Its
    /// <c>item_evolving_cost_mul</c> is not the pool's <c>gold_mul</c> - it is unit attribute 223 on
    /// the caster, a per-mille discount that is zero unless a buff grants one.
    /// </para>
    /// </remarks>
    private static long EvolvingCost(Character owner, ItemRndAttrCategory category, EquipItem item,
        uint offeredExp)
    {
        var formula = FormulaManager.Instance.GetFormula((uint)FormulaKind.ItemEvolvingCost);
        if (formula == null)
            return 0;

        var accumulated = AccumulateBandPrice(category, item, offeredExp);
        if (accumulated <= 0)
            return 0;

        var parameters = new Dictionary<string, double>
        {
            { "item_evolving_value", accumulated },
            { "item_level", item.Template?.Level ?? 0 },
            { "item_evolving_cost_mul", owner.CalculateWithBonuses(0d, UnitAttribute.ItemEvolvingCostMul) }
        };

        var cost = formula.Evaluate(parameters);
        if (double.IsNaN(cost) || cost <= 0)
            return 0;

        cost = Math.Floor(cost + 0.5d);
        return cost > long.MaxValue ? long.MaxValue : (long)cost;
    }

    /// <summary>
    /// Spreads the experience across the grades it will travel and totals what each charges for its
    /// own slice.
    /// </summary>
    private static long AccumulateBandPrice(ItemRndAttrCategory category, EquipItem item, uint offeredExp)
    {
        var remaining = (long)item.EvolvingExp + offeredExp;
        // Only the first slice is discounted by what the piece already carries; every later grade is
        // entered from nothing.
        var banked = (long)item.EvolvingExp;
        var grade = item.Grade;
        var topOrder = GradeOrderOf((byte)Math.Max(0, category.MaxEvolvingGrade));
        var accumulated = 0L;

        // The ladder is short enough that a bound only guards against a grade table that loops.
        for (var step = 0; step < 32 && remaining > 0; step++)
        {
            var property = ItemEnchantGameData.Instance.GetRndAttrProperty(category.Id, grade);
            if (property == null)
                break;

            var chunk = Math.Min((long)property.GradeExp, remaining);
            accumulated += (chunk - banked) * property.GoldMul / 1000;
            banked = 0;

            var order = GradeOrderOf(grade);
            if (order > topOrder)
                break;
            if (order == topOrder && remaining >= property.GradeExp)
                break;

            remaining -= property.GradeExp;

            var next = ItemManager.Instance.GetGradeTemplateByOrder(order + 1);
            if (next == null)
                break;
            grade = (byte)next.Grade;
        }

        return accumulated;
    }

    /// <summary>
    /// Where a grade sits on the ladder. Crude carries the id above Basic but the order below it, so
    /// the id cannot be walked directly.
    /// </summary>
    private static int GradeOrderOf(byte grade)
    {
        return ItemManager.Instance.GetGradeTemplate(grade)?.GradeOrder ?? grade;
    }

    /// <summary>
    /// Takes the fee in whatever the pool charges in. Only the coin currencies are wired; anything
    /// else is refused rather than quietly waived, so a pool that charges something this server
    /// cannot take does not become a free ride.
    /// </summary>
    private static bool TryCharge(Character owner, ItemRndAttrCategory category, long cost)
    {
        switch ((ContentCurrencyType)category.CurrencyId)
        {
            case ContentCurrencyType.Gold:
            case ContentCurrencyType.GoldWithAaPoint:
                if (owner.Money < cost)
                    return false;
                return owner.SubtractMoney(SlotType.Inventory, cost, ItemTaskType.Evolving);
            default:
                Logger.Warn("ItemEvolving: pool {0} charges currency {1}, which is not wired",
                    category.Id, category.CurrencyId);
                return false;
        }
    }
}
