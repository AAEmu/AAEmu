using System;

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
        Logger.Debug($"GainLootPackItemEffect: LootPackId={LootPackId}");

        var pack = LootGameData.Instance.GetPack(LootPackId);
        if (pack == null || pack.Loots.Count <= 0)
            return;

        if (!ConsumeSourceItem && ConsumeCount == 0)
        {
            // the tractor collects water
            character.Inventory.Bag.ConsumeItem(ItemTaskType.ConsumeSkillSource, ConsumeItemId, ConsumeCount, null);
            pack.GiveLootPack(character, ItemTaskType.SkillEffectGainItem);
            Logger.Debug($"GainLootPackItemEffect {LootPackId}");
            return;
        }

        if (casterObj is not SkillItem skillItem)
            return;

        var sourceItem = character.Inventory.Bag.GetItemByItemId(skillItem.ItemId);
        if (sourceItem == null)
        {
            Logger.Warn($"Invalid loot result items {skillItem.ItemId} in lootpack {LootPackId}");
            return;
        }

        if (ConsumeSourceItem)
        {
            character.Inventory.Bag.RemoveItem(ItemTaskType.ConsumeSkillSource, sourceItem, true);
        }
        else
        {
            character.Inventory.Bag.ConsumeItem(ItemTaskType.ConsumeSkillSource, ConsumeItemId, ConsumeCount, null);
        }

        // Give the results
        pack.GiveLootPack(character, ItemTaskType.SkillEffectGainItem);

        Logger.Debug($"GainLootPackItemEffect {LootPackId}");
    }
}
