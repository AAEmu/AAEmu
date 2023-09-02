using System;
using System.Collections.Generic;
using System.Linq;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects
{
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
            if (caster is not Character character)
                return;

            if (casterObj is not SkillItem skillItem)
                return;
            
            var pack = LootGameData.Instance.GetPack(LootPackId);
            if ((pack == null) || (pack.Loots.Count <= 0))
                return;

            var sourceItem = character.Inventory.Bag.GetItemByItemId(skillItem.ItemId);
            if (sourceItem == null)
            {
                _log.Warn($"Invalid loot result items {skillItem.ItemId} in lootpack {LootPackId}");
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

            pack.GiveLootPack(character, ItemTaskType.SkillEffectGainItem);

            _log.Debug("GainLootPackItemEffect {0}", LootPackId);
        }
    }
}
