using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Features;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Units;

// The special effect class has to be named after the SpecialType for dispatch, which shadows the
// data type of the same name in Models.Game.Items.
using ChangeMappingRoute = AAEmu.Game.Models.Game.Items.ItemChangeMapping;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

/// <summary>
/// Awakening ("각성"), special effect 165, carried by an awakening scroll's use skill - e.g. scroll
/// 47866 (도약의 각성 주문서: 1단계) casting skill 42200.
/// </summary>
/// <remarks>
/// <para>
/// The scroll is the caster item, the equipment is the cast target, and <c>value1</c> names an
/// <c>item_change_mapping_groups</c> row. That group holds the routes: source item at a given grade
/// becomes a different item. Explorer's Greatsword 47783 at grade 4 becomes 47893.
/// </para>
/// <para>
/// The item is edited in place rather than replaced, so it keeps its id, slot and detail, and the
/// client is told through <see cref="SCItemChangeMappingResultPacket"/>, which carries the item both
/// before and after.
/// </para>
/// </remarks>
public class ItemChangeMapping : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.ItemChangeMapping;

    private const int ChanceScale = 10000;

    /// <summary>
    /// Whether awakening is enabled. Checked at the entry point, before any validation, RNG, reagent or
    /// item mutation, so that a disabled feature cannot half-run.
    /// </summary>
    /// <remarks>
    /// Fails closed: an absent feature set counts as disabled rather than as permission.
    /// <para>
    /// The feature shares its bit with Dwarf/Warborn character creation, so
    /// <see cref="Feature.dwarfWarborn"/> names the same switch. That is precisely why this check exists
    /// in code: whichever name the configuration happens to use, the gate is this call, not the spelling
    /// of the setting that turned the bit on.
    /// </para>
    /// </remarks>
    internal static bool IsFeatureEnabled(FeatureSet features) =>
        features is not null && features.Check(Feature.itemChangeMapping);

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
            return;

        if (!IsFeatureEnabled(FeaturesManager.Fsets))
        {
            Reject(character);
            return;
        }

        if (targetObj is not SkillCastItemTarget itemTarget)
        {
            Logger.Warn("ItemChangeMapping: unexpected target {0}", targetObj?.GetType().Name ?? "null");
            Reject(character);
            return;
        }

        Logger.Debug("ItemChangeMapping: cast tl={0} skill={1} target={2}",
            skill?.TlId, skill?.Template?.Id, itemTarget.Id);

        var group = ItemManager.Instance.GetChangeMappingGroup((uint)value1);
        if (group is null)
        {
            Logger.Warn("ItemChangeMapping: no mapping group {0} for skill {1}", value1, skill?.Template?.Id);
            Reject(character);
            return;
        }

        var item = character.Inventory.GetItemById(itemTarget.Id);
        if (item is null)
        {
            Reject(character);
            return;
        }

        var mapping = ItemManager.Instance.GetChangeMapping(group, item,
            (skillObject as SkillObjectItemChangeMapping)?.MappingId ?? 0);
        if (mapping is null)
        {
            // Wrong item for this scroll, or not yet at the grade the route needs.
            Reject(character, ErrorMessageType.NotEnoughRequiredItem);
            return;
        }

        var targetTemplate = ItemManager.Instance.GetTemplate(mapping.TargetItemId);
        if (targetTemplate is null)
        {
            Logger.Warn("ItemChangeMapping: mapping {0} targets unknown item {1}", mapping.Id, mapping.TargetItemId);
            Reject(character);
            return;
        }

        // Editing in place only works while the result is still the same kind of item. Every shipped
        // route keeps the kind (weapon to weapon, armor to armor); refuse rather than corrupt if not.
        if (targetTemplate.ClassType != item.Template.ClassType)
        {
            Logger.Warn("ItemChangeMapping: mapping {0} changes item class {1} -> {2}",
                mapping.Id, item.Template.ClassType.Name, targetTemplate.ClassType.Name);
            Reject(character);
            return;
        }

        // Do NOT consume here. Skill.ApplyEffects already burns consume_item_id x consume_item_count
        // for every effect once the effects have run (as ItemTaskType.SkillEffectConsumption), and it
        // does so whether or not consume_source_item is set - so the Hiram scroll's ten are already
        // accounted for. Consuming again took twenty. All this needs to do is refuse the attempt when
        // the player cannot pay, because that generic path silently skips instead of failing.
        if (!HasRequiredScrolls(character, skill))
        {
            Reject(character, ErrorMessageType.NotEnoughRequiredItem);
            return;
        }

        var before = new PacketStream();
        item.Write(before);

        var equipItem = item as EquipItem;
        var chance = SuccessChance(group, equipItem);
        var succeeded = chance >= ChanceScale || Random.Shared.Next(0, ChanceScale) < chance;

        if (succeeded)
            Awaken(item, equipItem, mapping, group, targetTemplate);
        else if (equipItem is not null && group.FailBonus > 0)
        {
            // The client prints this byte straight out as "Bonus Success Rate +N%", so it is stored as
            // whole percent while the group's numbers are basis points (success 1000 = 10%).
            var bonus = equipItem.MappingFailBonus + group.FailBonus / 100;
            equipItem.MappingFailBonus = (byte)Math.Min(bonus, byte.MaxValue);
        }

        item.IsDirty = true;

        // The result packet only drives the notice - its handler builds a message from its own copy of
        // the item and never touches the bag. Re-set the slot from a full item body so the client picks
        // up the new template, grade and detail; that is the Take task's documented behaviour.
        // UpdateGearBonuses only runs on equip/unequip, so an item awakened while worn would keep
        // scoring as the item it used to be until it was re-equipped or the character relogged.
        if (item.SlotType == SlotType.Equipment)
            character.UpdateGearBonuses(null, null);

        character.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.GradeEnchant, [new ItemAdd(item)], []));

        // Retail shows an "Awakening Results" window either way - Success or Failed - and the client
        // will not re-enable Confirm until it arrives, so this is sent on every attempt. The handler
        // picks the outcome from `result == 0`, which is why a failure sent as 0
        // previously rendered as a success.
        character.SendPacket(new SCItemChangeMappingResultPacket(
            before.GetBytes(), item, mapping.Id, (byte)(succeeded ? 0 : 1)));
    }

    private static void Awaken(Item item, EquipItem equipItem, ChangeMappingRoute mapping,
        ItemChangeMappingGroup group, ItemTemplate targetTemplate)
    {
        var sourceCategory = ItemManager.Instance.GetRndAttrCategoryForItem(item);

        item.TemplateId = mapping.TargetItemId;
        item.Template = targetTemplate;

        var targetCategory = ItemManager.Instance.GetRndAttrCategoryForItem(item);

        // The grade does not carry over: the client re-earns it by replaying the EXP the item has
        // behind it against the NEW category's ladder. Explorer's Greatsword at
        // Arcane has 63 EXP behind it, and 63 buys only Grand on the 2T ladder - which is exactly
        // what the game shows. target_grade_id is the fallback for when that cannot be computed.
        if (equipItem is not null && group.EvolvingExpInherit && targetCategory is not null)
        {
            var totalExp = ItemManager.Instance.GetEvolvingTotalExp(sourceCategory, item.Grade, equipItem.EvolvingExp);
            var startGrade = ItemManager.Instance.GetEvolvingLadderStartGrade(targetCategory);
            var (grade, remainingExp) = ItemManager.Instance.SpendEvolvingExp(targetCategory, startGrade, totalExp);
            item.Grade = grade;
            equipItem.EvolvingExp = remainingExp;
        }
        else if (mapping.TargetGradeId >= 0)
        {
            item.Grade = (byte)mapping.TargetGradeId;
            if (equipItem is not null)
                equipItem.EvolvingExp = 0;
        }
        else if (equipItem is not null && !group.EvolvingExpInherit)
        {
            equipItem.EvolvingExp = 0;
        }

        if (equipItem is null)
            return;

        // The stored Synthesis Effect groups belong to the category the item just left, and would
        // resolve to the wrong attributes under the new one. Draw fresh for where it landed.
        equipItem.RndAttrGroupIds = ItemManager.Instance.RollRndAttrGroups(targetCategory, item.Grade);

        // The accumulated pity belonged to the item it used to be.
        equipItem.MappingFailBonus = 0;
    }

    /// <summary>
    /// Refuses the attempt. Cancelling matters as much as the message: Skill.ApplyEffects queues the
    /// scroll cost before effects run and spends it afterwards unless the cast was vetoed, so simply
    /// returning would take the scrolls for an awakening that never happened.
    /// </summary>
    private static void Reject(Character character, ErrorMessageType error = ErrorMessageType.Invalid)
    {
        character.SkillCancelled = true;
        if (error != ErrorMessageType.Invalid)
            character.SendErrorMessage(error);
    }

    /// <summary>
    /// Whether the player can pay for this attempt. The cost lives on the skill effect
    /// (<c>consume_item_id</c> / <c>consume_item_count</c>) - ten for the Hiram scroll, one for the
    /// Explorer scroll - and Skill.ApplyEffects is what actually takes them.
    /// </summary>
    private static bool HasRequiredScrolls(Character character, Skill skill)
    {
        foreach (var effect in skill?.Template?.Effects ?? [])
        {
            if (effect.ConsumeItemId == 0 || effect.ConsumeItemCount <= 0)
                continue;

            character.Inventory.Bag.GetAllItemsByTemplate(effect.ConsumeItemId, -1, out _, out var owned);
            if (owned >= effect.ConsumeItemCount)
                continue;

            Logger.Warn("ItemChangeMapping: {0} has {1}x item {2}, needs {3}",
                character.Name, owned, effect.ConsumeItemId, effect.ConsumeItemCount);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Base chance plus the pity earned by previous failures on this item.
    /// </summary>
    /// <remarks>
    /// <c>EquipItem.MappingFailBonus</c> is one byte holding whole percent - the client renders it
    /// verbatim as "Bonus Success Rate +N%", which is how it was identified. Group numbers are basis
    /// points, so a <c>fail_bonus</c> of 500 adds 5 per failure. Retail has been seen showing +4%,
    /// which does not divide by 5, so the step may not be uniform - worth another look if the rate
    /// drifts from the client's own display.
    /// </remarks>
    private static int SuccessChance(ItemChangeMappingGroup group, EquipItem equipItem)
    {
        var chance = group.Success + (equipItem?.MappingFailBonus ?? 0) * 100;
        return Math.Clamp(chance, 0, ChanceScale);
    }
}
