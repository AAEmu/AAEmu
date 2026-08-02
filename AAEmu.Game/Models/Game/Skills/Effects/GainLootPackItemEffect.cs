using AAEmu.Game.Core.Packets;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects;

/*
 * Original Authors: nikes, AAGene, Hexlulz
 * Modified by: ZeromusXYZ
 */

public class GainLootPackItemEffect : EffectTemplate
{
    public uint LootPackId { get; set; }
    public bool ConsumeSourceItem { get; set; }
    public uint ConsumeItemId { get; set; }
    public int ConsumeCount { get; set; }
    public bool InheritGrade { get; set; }

    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        // Ignore if not a player
        if (caster is not Character character)
            return;

        // Get Pack data
        var pack = LootGameData.Instance.GetPack(LootPackId);
        if (pack == null || pack.Loots.Count <= 0)
            return;

        // TODO: Find the related ActAbility
        var actAbility = ActabilityType.None;

        // Item-use skills (coin purses etc.): never grant loot if the source item is gone.
        // The effect-template flags often leave ConsumeSourceItem=false (consumption is on
        // skill_effects); the old early-out still GaveLootPack with no item check → infinite
        // client-ghost purses after the server had already deleted the real item.
        if (casterObj is SkillItem skillItem)
        {
            var sourceItem = character.Inventory.Bag.GetItemByItemId(skillItem.ItemId)
                             ?? character.Inventory.GetItemById(skillItem.ItemId);
            if (sourceItem == null && skillItem.ItemTemplateId != 0)
            {
                // Fallback: any bag stack of this template (ItemId desync / already partially consumed).
                character.Inventory.Bag.GetAllItemsByTemplate(skillItem.ItemTemplateId, -1, out var found, out _);
                sourceItem = found.Count > 0 ? found[0] : null;
            }

            if (sourceItem == null)
            {
                Logger.Warn(
                    "GainLootPackItemEffect {0}: SkillItem {1} (tpl {2}) missing — refusing loot",
                    LootPackId, skillItem.ItemId, skillItem.ItemTemplateId);
                return;
            }

            // Consume exactly one (or ConsumeCount) from the SkillItem stack — purses stack
            // (max_stack_size=1000). Full RemoveItem wiped the entire stack on a single use.
            var burn = ConsumeCount > 0 ? ConsumeCount : 1;
            var burned = character.Inventory.Bag.ConsumeItem(
                ItemTaskType.ConsumeSkillSource, sourceItem.TemplateId, burn, sourceItem);
            if (burned < burn)
            {
                Logger.Warn(
                    "GainLootPackItemEffect {0}: ConsumeItem burned {1}/{2} for {3} (tpl {4}) — refusing loot",
                    LootPackId, burned, burn, sourceItem.Id, sourceItem.TemplateId);
                return;
            }

            byte? inheritedGrade = InheritGrade ? sourceItem.Grade : null;
            pack.GiveLootPack(character, actAbility, ItemTaskType.SkillEffectGainItem, inheritedGrade: inheritedGrade);
            Logger.Info(
                "GainLootPackItemEffect {0}: consumed {1}x item {2} tpl {3}, granted loot",
                LootPackId, burn, sourceItem.Id, sourceItem.TemplateId);
            return;
        }

        if (!ConsumeSourceItem && ConsumeCount == 0)
        {
            // Non-item casters (e.g. tractor water collect) — no source item to validate.
            character.Inventory.Bag.ConsumeItem(ItemTaskType.ConsumeSkillSource, ConsumeItemId, ConsumeCount, null);
            pack.GiveLootPack(character, actAbility, ItemTaskType.SkillEffectGainItem);
            Logger.Debug($"GainLootPackItemEffect {LootPackId}");
            return;
        }

        Logger.Warn($"GainLootPackItemEffect {LootPackId}: expected SkillItem caster for consume path");
    }
}
