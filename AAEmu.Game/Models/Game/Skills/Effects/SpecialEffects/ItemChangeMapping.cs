using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Units;

// The effect class has to be named after its SpecialType for the reflection lookup in
// SpecialEffect.Apply, which collides with the recipe model of the same name.
using ItemChangeMappingData = AAEmu.Game.Models.Game.Items.ItemChangeMapping;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

/// <summary>
/// Awakening - the "Awakening" tab, 각성 in the data. Turns a piece of gear into the awakened item
/// its mapping group names, or fails and leaves the player a little closer to the next attempt.
/// </summary>
/// <remarks>
/// <para>
/// <c>value1</c> is the <c>item_change_mapping_groups</c> row the scroll belongs to; the group's
/// <c>success</c> / <c>disable</c> / <c>fail_bonus</c> are per 10000. Every failure adds
/// <c>fail_bonus</c> to the item's own accumulated bonus, which is why the item carries
/// <c>mappingFailBonus</c> in its detail block - retail's pity counter.
/// </para>
/// <para>
/// When the group is <c>selectable</c> the player picks which candidate to aim for and the client
/// sends that choice; the pick rides in the skill object's step field. Without a usable choice the
/// server rolls one of the candidates itself, which is also what a non-selectable group does.
/// </para>
/// </remarks>
public class ItemChangeMapping : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.ItemChangeMapping;

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
            Logger.Error("ItemChangeMapping: caster {0} is not a character", caster?.Id);
            return;
        }

        if (targetObj is not SkillCastItemTarget itemTarget)
        {
            Logger.Warn("ItemChangeMapping: target {0} is not an item", targetObj);
            skill.Cancelled = true;
            return;
        }

        if (owner.Inventory.GetItemById(itemTarget.Id) is not EquipItem equipItem)
        {
            Logger.Warn("ItemChangeMapping: item {0} not found or not equipment", itemTarget.Id);
            skill.Cancelled = true;
            return;
        }

        if (equipItem.EnchantDisabled)
        {
            owner.SendErrorMessage(ErrorMessageType.ItemCannotUse);
            skill.Cancelled = true;
            return;
        }

        var group = ItemEnchantGameData.Instance.GetChangeMappingGroup((uint)value1);
        if (group == null)
        {
            Logger.Warn("ItemChangeMapping: no mapping group {0}", value1);
            skill.Cancelled = true;
            return;
        }

        var candidates = ItemEnchantGameData.Instance.GetChangeMappings(group.Id, equipItem.TemplateId, equipItem.Grade);
        if (candidates.Count == 0)
        {
            // The scroll does not apply to this piece. The client filters this out itself, so this
            // only fires on a stale window.
            owner.SendErrorMessage(ErrorMessageType.ItemCannotUse);
            skill.Cancelled = true;
            return;
        }

        var mapping = PickCandidate(candidates, group, skillObject);
        var targetTemplate = ItemManager.Instance.GetTemplate(mapping.TargetItemId);
        if (targetTemplate == null)
        {
            Logger.Warn("ItemChangeMapping: mapping {0} points at unknown item {1}", mapping.Id, mapping.TargetItemId);
            skill.Cancelled = true;
            return;
        }

        // The accumulated pity bonus is what the awaken tab shows as "bonusRate" next to the base
        // success chance, so it goes out in the result packet either way.
        var bonusRate = equipItem.MappingFailBonus * group.FailBonus;
        var succeeded = Random.Shared.Next(10000) < group.Success + bonusRate;

        if (!succeeded)
        {
            var disabled = group.Disable > 0 && Random.Shared.Next(10000) < group.Disable;
            if (disabled)
                equipItem.EnchantDisabled = true;

            // Cap the counter at what a byte holds; a group with fail_bonus 0 never moves it anyway.
            if (group.FailBonus > 0 && equipItem.MappingFailBonus < byte.MaxValue)
                equipItem.MappingFailBonus++;

            equipItem.IsDirty = true;
            owner.SendPacket(new SCItemDetailUpdatedPacket(equipItem));
            owner.SendPacket(new SCItemChangeMappingResultPacket(equipItem, equipItem, bonusRate,
                disabled ? ItemChangeMappingResult.FailDisableEnchant : ItemChangeMappingResult.Fail));

            Logger.Debug("ItemChangeMapping: {0} failed on item {1} (group {2}, disabled {3}, fails {4})",
                owner.Name, equipItem.Id, group.Id, disabled, equipItem.MappingFailBonus);
            return;
        }

        if (targetTemplate.ClassType != equipItem.GetType())
        {
            // Every shipped mapping stays inside one item class (weapon to weapon, cape to cape).
            // Swapping across classes would leave the stat code casting the wrong template, so stop
            // rather than hand the player a broken item.
            Logger.Error("ItemChangeMapping: mapping {0} crosses item classes ({1} -> {2})",
                mapping.Id, equipItem.GetType().Name, targetTemplate.ClassType.Name);
            skill.Cancelled = true;
            return;
        }

        ApplySuccess(owner, equipItem, mapping, group, bonusRate);
    }

    /// <summary>
    /// Resolves which of the group's candidates the attempt aims at.
    /// </summary>
    /// <remarks>
    /// A selectable group lets the player choose, and the awaken tab sends that choice as the
    /// item_change_mappings row id. It is treated as a request rather than an instruction: the row
    /// is honoured only if it is genuinely one of the candidates already established for this group,
    /// item and grade, so a hand-built request cannot name a mapping off some other item.
    /// </remarks>
    private static ItemChangeMappingData PickCandidate(
        List<ItemChangeMappingData> candidates,
        ItemChangeMappingGroup group,
        SkillObject skillObject)
    {
        if (group.Selectable && skillObject is SkillObjectItemChangeMapping { MappingId: > 0 } choice)
        {
            var picked = candidates.Find(c => c.Id == choice.MappingId);
            if (picked != null)
                return picked;

            Logger.Warn("ItemChangeMapping: chosen mapping {0} is not a candidate for group {1}",
                choice.MappingId, group.Id);
        }

        return candidates[Random.Shared.Next(candidates.Count)];
    }

    /// <summary>
    /// Rewrites the item in place. Keeping the same item id means the socket, dye and look state ride
    /// along untouched and the piece stays in its slot; the client is told to drop the old entry and
    /// take the new one, which is what its two-item result packet is shaped for.
    /// </summary>
    private static void ApplySuccess(Character owner, EquipItem equipItem,
        ItemChangeMappingData mapping, ItemChangeMappingGroup group, int bonusRate)
    {
        var oldTemplateId = equipItem.TemplateId;

        // Read before the rewrite: the pool the piece is leaving is what its banked progress is
        // measured against.
        var sourceCategoryId = (equipItem.Template as EquipItemTemplate)?.RndAttrCategoryId ?? 0;

        // The client reads the old grade and gear score off the result packet's first item, so it
        // needs the piece as it was. Build a throwaway stand-in before the rewrite - no id is handed
        // out for it, it never enters an inventory and it lives only as long as the packet.
        var oldSnapshot = ItemManager.Instance.Create(oldTemplateId, 1, equipItem.Grade, false) ?? equipItem;

        equipItem.TemplateId = mapping.TargetItemId;
        equipItem.Template = ItemManager.Instance.GetTemplate(mapping.TargetItemId);

        // A successful awakening resets the pity counter, and the group decides whether synthesis
        // progress survives the change.
        equipItem.MappingFailBonus = 0;
        if (!group.EvolvingExpInherit)
        {
            equipItem.EvolvingExp = 0;
            equipItem.RndAttrGroupIds = new uint[EquipItem.RndAttrSlots];
        }

        // The experience comes along untouched - it is progress inside a grade, and the piece keeps
        // it - while the grade is where the new tier starts rather than the one the piece came in at.
        // Each tier's pool begins granting effects one rung higher than the last, and that rung is
        // the entry point: the main-quest one-handed chain reads 2, 3, 4 across its tiers, which is
        // the Arcane to Grand, Heroic to Rare and Unique to Arcane the awakening window previews.
        // Only a mapping that names a grade outright overrides it, and none of the shipped ones do.
        var targetCategoryId = (equipItem.Template as EquipItemTemplate)?.RndAttrCategoryId ?? 0;
        if (mapping.TargetGradeId >= 0)
        {
            equipItem.Grade = (byte)mapping.TargetGradeId;
        }
        else if (sourceCategoryId != 0 && targetCategoryId != 0)
        {
            // Everything the piece has ever banked carries over, and the new tier's ladder decides
            // what that total is worth there. The ladders are cut to line up: a Brilliant Jerkin
            // maxed at Unique has banked 1607, and 1607 is exactly what the Hiram Jerkin's ladder
            // charges to reach Arcane - which is the grade the awakening window previews. The stored
            // value is only the progress inside a grade, so the total is reassembled from the grade
            // the piece is leaving and split again over the grade it arrives at.
            var carriedExp = ItemEnchantGameData.Instance
                .GetCumulativeExp(sourceCategoryId, equipItem.Grade, equipItem.EvolvingExp);

            var placed = ItemEnchantGameData.Instance.PlaceCumulativeExp(targetCategoryId, carriedExp);
            equipItem.Grade = placed.Grade;
            equipItem.EvolvingExp = placed.SectionExp;
        }

        // The effects survive the change. The awakening window previews them as "Spirit 27 to 44" -
        // the same lines re-valued for the grade the piece lands on, which works because an item
        // stores the effect's group and not its magnitude. Only what the new pool has no room for is
        // dropped, and the change attempts ride along too: they belong to the piece, not the tier.
        if (targetCategoryId != 0)
        {
            // Re-home the effects into the new pool before anything else looks at them. A group id
            // only means something inside the pool it came from - both its magnitude and the bundle
            // that owns it - so an id carried across unchanged is an effect the new pool cannot
            // value and no bundle counts as its own, which is what lets a bundle grant a second of
            // what the piece already wears.
            var carried = ItemEnchantGameData.Instance
                .TranslateGroupsToCategory(equipItem.UsedRndAttrGroupIds, targetCategoryId);

            var cap = ItemEnchantGameData.Instance.GetRndAttrCap(targetCategoryId, equipItem.Grade);
            var groupIds = new uint[EquipItem.RndAttrSlots];
            for (var i = 0; i < groupIds.Length; i++)
                groupIds[i] = i < cap && i < carried.Count ? carried[i] : 0u;
            equipItem.RndAttrGroupIds = groupIds;

            ItemEvolving.TopUpAttributes(equipItem, targetCategoryId);
        }

        // Tempering cannot survive past the new template's own ceiling.
        var maxScale = equipItem.Template?.MaxEnchantScaleId ?? 0;
        if (equipItem.EnchantScale > maxScale)
            equipItem.EnchantScale = maxScale;

        equipItem.IsDirty = true;

        // The item keeps its id and its slot and only changes what it is, so it is re-stated rather
        // than swapped: the Take body carries the full item and overwrites the entry already in that
        // slot. Pairing it with a removal, which is the obvious way to express "this became something
        // else", destroys the item instead - Seize names the id in its remove field, and the client
        // acts on that whatever follows it in the same packet.
        owner.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.EnchantMagical, [new ItemAdd(equipItem)], []));

        // Gear bonuses are totalled when a piece is equipped and not again, so a change made to a
        // piece already being worn never reached the character sheet. Re-total here; the piece is
        // only in the sum at all while it sits in the equipment container.
        if (equipItem.SlotType == SlotType.Equipment)
            owner.UpdateGearBonuses(null, null);

        owner.SendPacket(new SCItemChangeMappingResultPacket(oldSnapshot, equipItem, bonusRate,
            ItemChangeMappingResult.Success));

        Logger.Debug("ItemChangeMapping: {0} awakened item {1} from {2} to {3} (group {4})",
            owner.Name, equipItem.Id, oldTemplateId, equipItem.TemplateId, group.Id);
    }
}
